using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// A dark wall with little explosions going off on the beat, one at a time,
    /// in a different place each time.
    ///
    /// WHAT ONE BURST LOOKS LIKE
    ///
    /// A single bulb lights. The four around it join it, making a plus. The
    /// middle drops out, leaving the plus alone. Then that goes too and the wall
    /// is dark again, ready for the next beat.
    ///
    /// That is the smallest one. Bigger bursts do the same thing with more rings,
    /// so the ring travels outward like a ripple rather than just appearing and
    /// vanishing.
    ///
    /// HOW THE MUSIC CHOOSES WHICH BURST
    ///
    /// Two separate readings, deliberately kept apart so each is easy to see
    /// working on its own.
    ///
    /// The low end sets the SIZE. A heavy kick drum throws a wide ripple most of
    /// the way across the wall; a light one makes a small plus.
    ///
    /// Whether the low or the high end is leading sets which way the star POINTS.
    /// Bass-led beats throw a star pointing north, south, east and west, whose
    /// smallest form is the four-bulb plus. Beats led by the top end - a hi-hat,
    /// a snare, a bright synth stab - throw the same star turned to point at the
    /// corners, whose smallest form is a four-bulb X. Small bursts of the two
    /// kinds therefore cannot be mistaken for one another, which is most of the
    /// point of having two.
    ///
    /// WHERE THE BEAT COMES FROM
    ///
    /// Either beats actually heard in the music or a metronome running at the
    /// detected tempo, whichever the user has chosen. The choice is made once in
    /// the interface and read through EffectContext.BeatCount, so nothing here
    /// knows or cares which is in force. See BeatSource.
    ///
    /// Both readings come from band levels that are each measured against their
    /// own recent history. That is what makes "the bass is bumping" mean
    /// something even in a quiet passage, and it is why a hi-hat can out-shout a
    /// kick drum here when it is the hi-hat that is actually doing the work. See
    /// SpectrumAnalyser.
    ///
    /// WHY THIS ONE HOLDS STATE, WHEN MOST EFFECTS MUST NOT
    ///
    /// Nearly every effect in this project is a pure function of time: hand it
    /// the same moment twice and it draws the same picture, with nothing
    /// remembered in between. That rule is what stops random effects dissolving
    /// into flicker when the screen redraws far more often than they change.
    ///
    /// This one has to remember something, because a burst is an event rather
    /// than a position. Where it appeared and when it started cannot be worked
    /// out from the current time alone - they were decided at the moment a beat
    /// arrived, which may have been several redraws ago.
    ///
    /// The rule is honoured where it actually matters, though, and that is worth
    /// being precise about. What the rule really protects is that the same
    /// question asked twice gives the same answer, and it still does here:
    ///
    /// - A new burst only ever starts when the beat COUNT changes. Drawing the
    ///   same moment twice sees the same count the second time and starts
    ///   nothing, so the second answer matches the first.
    /// - Where a burst lands is derived from the beat number rather than drawn
    ///   fresh from a shared generator, so beat 40 always lands in the same
    ///   place. Nothing about the picture depends on how many times it was asked.
    ///
    /// EqBumperEffect holds state for much the same reason, and the note there
    /// says the same thing: audio-reactive effects were never pure functions of
    /// time to begin with.
    ///
    /// A NOTE ON THE POWER BUDGET
    ///
    /// Comfortable. A burst is at most about eighteen bulbs at its widest and the
    /// wall is dark between them, so this sits nowhere near the current limit
    /// that holding all thirty-five lit would approach.
    /// </summary>
    public sealed class StarburstEffect : IWallEffect
    {
        /// <summary>
        /// The two kinds of burst. Both are eight-pointed stars; they differ in
        /// which four points lead and which four trail.
        ///
        /// HOW A STAR IS MADE OUT OF RINGS, AND WHAT THIS REPLACED
        ///
        /// Every bulb sits on one of eight arms - four straight (up, down, left,
        /// right) and four diagonal. A star, as opposed to a plain diamond or
        /// square, is what you get when one set of arms reaches further out than
        /// the other. Holding one set back by a single step is enough: the points
        /// stick out and the sides between them fall inward instead of running
        /// straight from point to point.
        ///
        /// Two earlier attempts, both worth remembering.
        ///
        /// The first was a solid diamond - every bulb an equal number of
        /// up-down-left-right moves from the middle. It expanded correctly and
        /// looked dull, because the edge between any two points is a perfectly
        /// straight diagonal line. It reads as a growing lozenge rather than as
        /// anything bursting.
        ///
        /// The second put all eight arms at the same distance. That is fine
        /// further out, but at one step from the middle the eight arms ARE the
        /// eight bulbs surrounding it - so the shape was a filled 3x3 square with
        /// the middle switched off, which is not a star by any reading. Small
        /// bursts and the last frame of large ones both ended on it. That was not
        /// a bug in the drawing so much as the shape being geometrically
        /// impossible at that size, which is why the fix was to change the shape
        /// rather than to special-case the radius.
        /// </summary>
        private enum BurstShape
        {
            /// <summary>
            /// Straight arms lead, diagonals trail one step behind. Points north,
            /// south, east and west. Its smallest form is the four-bulb plus.
            /// </summary>
            Star,

            /// <summary>
            /// Diagonals lead, straight arms trail one step behind. The same
            /// star turned to point at the corners. Its smallest form is a
            /// four-bulb X, which is why a small burst of this kind cannot be
            /// mistaken for a small burst of the other.
            /// </summary>
            Spark
        }

        /// <summary>
        /// How wide the travelling ring is, in bulbs.
        ///
        /// This is what makes one burst look like a ripple rather than a series
        /// of separate rings. At 0.75 the front is briefly touching two rings at
        /// once as it passes between them, which is what produces the
        /// middle-then-middle-and-plus-then-plus sequence rather than a hard cut
        /// from one to the next.
        /// </summary>
        private const double RingWidth = 0.75;

        /// <summary>
        /// How long a beat is assumed to last when the tempo is not yet known.
        ///
        /// Half a second is 120 beats a minute, which is a fair guess for most
        /// music and only matters for the first few seconds of a track.
        /// </summary>
        private const double AssumedBeatSeconds = 0.5;

        /// <summary>
        /// What share of the gap between beats a burst is allowed to fill.
        ///
        /// Below 1 on purpose, so each burst has finished before the next one
        /// starts and the wall reads as separate explosions rather than as one
        /// continuous churn.
        /// </summary>
        private const double BurstShareOfBeat = 0.85;

        /// <summary>The shortest and longest a burst may last, in seconds.</summary>
        private const double MinimumBurstSeconds = 0.12;
        private const double MaximumBurstSeconds = 0.70;

        /// <summary>
        /// How hard the low end has to be hitting for a medium or a large burst.
        ///
        /// Thresholds rather than a smooth scale because there are only three
        /// sizes available on a wall this small, and a smooth scale would spend
        /// most of its time hovering between two of them.
        /// </summary>
        private const double MediumBurstBass = 0.35;
        private const double LargeBurstBass = 0.70;

        /// <summary>
        /// The furthest out any burst travels.
        ///
        /// Three rings reaches most of the way across a wall this size, so there
        /// is nothing to be gained by allowing more.
        /// </summary>
        private const int LargestRadius = 3;

        /// <summary>The beat number the last burst was started for.</summary>
        private int _lastBeatCount;

        /// <summary>Whether a burst is currently running.</summary>
        private bool _hasBurst;

        /// <summary>Where the current burst is centred.</summary>
        private int _centreRow;
        private int _centreColumn;

        /// <summary>The effect time at which the current burst began.</summary>
        private double _startSeconds;

        /// <summary>How long the current burst runs for.</summary>
        private double _burstSeconds = AssumedBeatSeconds;

        /// <summary>How many rings out the current burst travels.</summary>
        private int _maximumRadius = 1;

        /// <summary>Which of the two shapes the current burst is drawing.</summary>
        private BurstShape _shape = BurstShape.Star;

        /// <inheritdoc />
        public string DisplayName => "Starburst";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        /// <inheritdoc />
        public EffectControl Controls => EffectControl.BeatSource;

        /// <inheritdoc />
        public string Description =>
            "Little explosions pop up around a dark wall, one on each beat. " +
            "Heavy bass throws a wide ripple; a bright hi-hat throws a small " +
            "spiky one. Start audio capture to make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            if (!context.IsAudioActive)
            {
                DrawWaitingPattern(target);
                Forget();
                return;
            }

            // Effect time restarts from zero whenever the effect is reselected,
            // which would leave a burst that began at, say, 40 seconds looking
            // like it starts 40 seconds in the future. Noticing time run
            // backwards and dropping the burst is simpler than trying to rescue
            // it, and the worst case is one missed explosion.
            if (context.TimeSeconds < _startSeconds)
            {
                Forget();
            }

            StartBurstIfBeatArrived(context);

            if (!_hasBurst)
            {
                return;
            }

            DrawBurst(context, target);
        }

        /// <summary>
        /// Shows something still when nothing is being listened to.
        ///
        /// A small plus in the middle: the smallest burst this effect draws,
        /// frozen. It says "running, waiting for music" and hints at what is
        /// about to happen, without inventing any motion that could be mistaken
        /// for a response to sound.
        ///
        /// The other audio effects show a single lit row for this. Same idea,
        /// said in this effect's own vocabulary - what matters is that it is
        /// static and clearly not pretending to have heard anything.
        /// </summary>
        private static void DrawWaitingPattern(WallFrame target)
        {
            int middleRow = WallFrame.Rows / 2;
            int middleColumn = WallFrame.Columns / 2;

            target.SetCell(middleRow, middleColumn, true);
            target.SetCell(middleRow - 1, middleColumn, true);
            target.SetCell(middleRow + 1, middleColumn, true);
            target.SetCell(middleRow, middleColumn - 1, true);
            target.SetCell(middleRow, middleColumn + 1, true);
        }

        /// <summary>
        /// Throws away the current burst.
        /// </summary>
        private void Forget()
        {
            _hasBurst = false;
            _lastBeatCount = 0;
            _startSeconds = 0.0;
        }

        /// <summary>
        /// Starts a new burst if a beat has been detected since the last one.
        ///
        /// Watching the COUNT rather than the time since the last beat is what
        /// makes this reliable. A time would have to be caught inside some window
        /// after the beat, and the engine reads these snapshots on its own
        /// schedule - so a short window could be stepped over entirely and a long
        /// one could be seen twice. A count that has changed means exactly one
        /// thing, however often it is looked at.
        /// </summary>
        private void StartBurstIfBeatArrived(EffectContext context)
        {
            // Whichever kind of beat the user picked - real ones heard in the
            // music, or the metronome running at the detected tempo. The effect
            // does not need to know which; see BeatSource.
            int beatCount = context.BeatCount;

            if (beatCount == _lastBeatCount)
            {
                return;
            }

            _lastBeatCount = beatCount;

            // Tied to the beat number rather than taken from a shared generator,
            // so asking about the same moment twice puts the burst in the same
            // place both times. See the note on state at the top of the class.
            Random random = context.CreateRandomForStep(beatCount);

            // Anywhere at all, edges included. A burst centred in a corner simply
            // has most of itself off the wall, which is the intended look rather
            // than a case to avoid - DrawBurst skips anything out of bounds.
            _centreRow = random.Next(WallFrame.Rows);
            _centreColumn = random.Next(WallFrame.Columns);

            ChooseSizeAndShape(context);

            _burstSeconds = ChooseBurstLength(context);
            _startSeconds = context.TimeSeconds;
            _hasBurst = true;
        }

        /// <summary>
        /// Reads the frequency bands to decide how big this burst is and which
        /// shape it draws.
        /// </summary>
        private void ChooseSizeAndShape(EffectContext context)
        {
            // The bottom two bands are the kick drum and the bass line. The top
            // three are where hi-hats, cymbals and bright synths live. The middle
            // two are deliberately ignored: most instruments have something
            // there, so it is the worst place to look to tell one hit from
            // another.
            double bass = Math.Max(
                context.Audio.GetBandLevel(0),
                context.Audio.GetBandLevel(1));

            double treble = Math.Max(
                context.Audio.GetBandLevel(4),
                Math.Max(
                    context.Audio.GetBandLevel(5),
                    context.Audio.GetBandLevel(6)));

            if (bass >= LargeBurstBass)
            {
                _maximumRadius = LargestRadius;
            }
            else if (bass >= MediumBurstBass)
            {
                _maximumRadius = 2;
            }
            else
            {
                _maximumRadius = 1;
            }

            if (treble > bass)
            {
                _shape = BurstShape.Spark;
            }
            else
            {
                _shape = BurstShape.Star;
            }
        }

        /// <summary>
        /// Works out how long this burst should last, so that it has finished
        /// before the next beat is due.
        ///
        /// A bigger burst covers more ground in the same time rather than taking
        /// longer, which is both what keeps the timing right and, as it turns
        /// out, what makes a big hit feel more violent than a small one.
        ///
        /// A note on an interaction worth knowing about. The gap between beats is
        /// measured in real seconds while the burst is animated in effect time,
        /// which the speed slider scales. Turn the speed up and bursts finish
        /// early, leaving a longer dark gap; turn it down and a burst is still
        /// going when the next beat arrives, at which point it is simply replaced.
        /// Neither looks broken, and only one burst is ever on the wall, so this
        /// is left as it is rather than fought.
        /// </summary>
        private static double ChooseBurstLength(EffectContext context)
        {
            double beatSeconds = AssumedBeatSeconds;

            if (context.Audio.TempoBpm > 0.0)
            {
                beatSeconds = 60.0 / context.Audio.TempoBpm;
            }

            return Math.Clamp(
                beatSeconds * BurstShareOfBeat,
                MinimumBurstSeconds,
                MaximumBurstSeconds);
        }

        /// <summary>
        /// Draws the current burst at whatever point through its life it is.
        /// </summary>
        private void DrawBurst(EffectContext context, WallFrame target)
        {
            double elapsed = context.TimeSeconds - _startSeconds;

            // How far the ring has travelled from the middle.
            //
            // EVERY BURST RIPPLES AT THE SAME SPEED, WHATEVER ITS SIZE
            //
            // The speed is set so that the LARGEST burst exactly fills the time
            // available. Smaller ones travel at that same speed and therefore
            // simply finish sooner, leaving a longer dark gap before the next
            // beat.
            //
            // The first version instead stretched each burst to fill the whole
            // gap, on the reasoning that the wall should stay busy. It looked
            // wrong and it took printing the frames out to see why. A one-ring
            // burst had to cover two steps rather than four, so it crawled - the
            // plus appeared and then sat there unchanged for three quarters of
            // the beat. It read as a blinking plus rather than as anything
            // bursting. A big hit and a light one also rippled at visibly
            // different speeds, which made the small one look like a different
            // effect rather than a smaller version of the same one.
            double ringsPerSecond = (LargestRadius + 1.0) / _burstSeconds;
            double front = elapsed * ringsPerSecond;

            if (front > _maximumRadius + RingWidth)
            {
                _hasBurst = false;
                return;
            }

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    int rowsAway = Math.Abs(row - _centreRow);
                    int columnsAway = Math.Abs(column - _centreColumn);

                    if (!IsOnShape(rowsAway, columnsAway, out int distance))
                    {
                        continue;
                    }

                    if (distance > _maximumRadius)
                    {
                        continue;
                    }

                    // Lit only while the travelling ring is passing over it.
                    if (Math.Abs(distance - front) < RingWidth)
                    {
                        target.SetCell(row, column, true);
                    }
                }
            }
        }

        /// <summary>
        /// Says whether a bulb is part of the current shape at all, and if so
        /// which ring of the burst it belongs to.
        ///
        /// A bulb has to sit on one of the eight arms to be part of a star at
        /// all - anything in the gaps between arms is never lit, and that is what
        /// makes the sides fall inward rather than running straight from point to
        /// point.
        ///
        /// The ring a bulb belongs to is then its distance along its own arm,
        /// with the TRAILING set of arms pushed out by one. That single step is
        /// the whole difference between a star and a plain ring: by the time the
        /// leading points have reached three steps out, the trailing ones are
        /// only at two.
        ///
        /// WHY EVERY BULB ON THE WALL IS OFFERED TO THIS
        ///
        /// Working outward from the middle instead would be fewer sums, but it
        /// would mean generating coordinates that may be off the wall and then
        /// checking each one - and WallFrame.SetCell throws rather than quietly
        /// ignoring a bulb that does not exist. Walking the wall and asking each
        /// real bulb whether it belongs cannot produce an out-of-bounds
        /// coordinate at all, which is the whole reason a burst can be centred in
        /// a corner and simply show the part of itself that fits.
        ///
        /// Thirty-five sums per frame is nothing.
        /// </summary>
        private bool IsOnShape(int rowsAway, int columnsAway, out int ring)
        {
            // The middle is where every burst starts, whichever way it points.
            if (rowsAway == 0 && columnsAway == 0)
            {
                ring = 0;
                return true;
            }

            bool onStraightArm = rowsAway == 0 || columnsAway == 0;
            bool onDiagonalArm = rowsAway == columnsAway;

            // Anything in the gaps between the eight arms is never part of a
            // star, and that is what makes the sides fall inward.
            if (!onStraightArm && !onDiagonalArm)
            {
                ring = 0;
                return false;
            }

            // Straight arms lead for a Star, diagonals lead for a Spark.
            bool onLeadingArm = _shape == BurstShape.Star ? onStraightArm : onDiagonalArm;

            // Diagonal steps are counted as steps, not as bulbs crossed, so a
            // diagonal arm reaches its point at the same moment a straight one
            // does rather than lagging by the length of the hypotenuse.
            int stepsOut = onStraightArm
                ? Math.Max(rowsAway, columnsAway)
                : rowsAway;

            if (onLeadingArm)
            {
                ring = stepsOut;
                return true;
            }

            // A TRAILING ARM NEVER USES ITS INNERMOST BULB, AND THIS IS LOAD
            // BEARING RATHER THAN A TASTE DECISION.
            //
            // The travelling ring is wide enough to be touching two rings at
            // once as it passes between them - that overlap is what makes a
            // burst look like a ripple instead of a series of separate rings.
            //
            // But the bulb one step out along a trailing arm is a diagonal
            // neighbour of the middle, and the bulbs one step out along the
            // leading arms are its straight neighbours. Light both while the
            // middle itself has gone dark and the result is all eight bulbs
            // around a dark one: the hollow 3x3 square this shape exists to
            // avoid. It came back exactly this way after the first attempt at
            // fixing it, which is why the rule is stated here as an invariant
            // rather than left to fall out of the arithmetic.
            //
            // Leaving that bulb out costs nothing visually. A burst small enough
            // to have reached only one step is drawn as a plain plus or X, which
            // is what it should look like at that size anyway.
            if (stepsOut < 2)
            {
                ring = 0;
                return false;
            }

            ring = stepsOut + 1;
            return true;
        }
    }
}
