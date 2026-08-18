using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Which way the bars lie in a Fill and Clear.
    /// </summary>
    public enum FillAxis
    {
        /// <summary>
        /// Horizontal bars, spreading up and down from the middle row.
        /// </summary>
        Rows,

        /// <summary>
        /// Vertical bars, spreading left and right from the middle column.
        /// </summary>
        Columns
    }

    /// <summary>
    /// The wall fills from the middle outward, then empties the same way.
    ///
    /// The middle bar lights, then the two either side of it, then the two beyond
    /// those, until the wall is full. Then the middle goes out, then the two
    /// either side of it, and so on until the wall is dark again - so the
    /// emptying spreads outward exactly as the filling did, leaving a hollow that
    /// grows rather than a wall that drains from its edges.
    ///
    /// TWO WAYS TO PACE IT, AND THEY LOOK QUITE DIFFERENT
    ///
    /// The same sequence of pictures, timed two ways. Which one is in force is
    /// the user's choice; see FillPacing.
    ///
    /// ONE STEP PER BEAT is slow and deliberate. Every beat moves the wall on by
    /// one picture, so a whole cycle takes as many beats as it has pictures.
    /// Counting outward from the middle, a wall five tall has three positions
    /// (the middle bar, one out, two out) and one seven wide has four - and
    /// filling uses each once while emptying uses each again:
    ///
    ///     Rows    (5 tall)  ->  3 + 3  =  6 beats for a full cycle
    ///     Columns (7 wide)  ->  4 + 4  =  8 beats
    ///
    /// At 120 beats a minute that is a three or four second cycle. The movement
    /// IS the beat.
    ///
    /// A WHOLE SWEEP PER BEAT is punchier. One beat runs the entire fill in a
    /// quick run of pictures and then holds the wall full; the next beat runs the
    /// entire clear and holds it dark. Two beats for a complete cycle whatever
    /// the wall's size, and the beat is the moment a sweep is LAUNCHED rather
    /// than the moment the wall moves - the same relationship a Starburst has
    /// with the beat that threw it.
    ///
    /// WHAT IS REMEMBERED, AND WHEN
    ///
    /// Stepping one picture per beat needs no memory at all: the beat number
    /// alone says which picture to draw, since dividing it by the number of
    /// pictures in a cycle leaves the position as the remainder. Checkerboard
    /// Switch works exactly this way, with a cycle two pictures long instead of
    /// six.
    ///
    /// Sweeping does need memory, because a sweep is an EVENT rather than a
    /// position - when it started was decided at the moment a beat arrived,
    /// possibly many redraws ago, and cannot be recovered from the current time.
    /// See AdvanceSweep, including why that is still safe under the
    /// repeatability rule.
    ///
    /// A NOTE ON THE POWER BUDGET
    ///
    /// Stepping, the whole wall is lit for one beat in six or eight - half a
    /// second at a time with three or four seconds between. Sweeping, it is lit
    /// for most of every second beat, since the wall holds full while waiting for
    /// the beat that will clear it. Both are comfortable; sweeping is the heavier
    /// of the two and is closer to a hold than to a flash.
    /// </summary>
    public sealed class FillAndClearEffect : IWallEffect
    {
        /// <summary>
        /// What share of a beat a whole sweep takes, when a beat is worth a whole
        /// sweep rather than one step.
        ///
        /// Half, so the sweep runs briskly and then holds - full, or dark - for
        /// the rest of the beat before the next one launches the return trip.
        /// Filling the whole beat would leave no pause at all and the wall would
        /// never be seen at rest at either end.
        /// </summary>
        private const double SweepShareOfBeat = 0.5;

        /// <summary>The shortest and longest a sweep may take, in seconds.</summary>
        private const double MinimumSweepSeconds = 0.10;
        private const double MaximumSweepSeconds = 0.60;

        /// <summary>
        /// How long one beat is assumed to last before a tempo is known.
        /// </summary>
        private const double AssumedBeatSeconds = 0.5;

        /// <summary>Which way the bars lie.</summary>
        private readonly FillAxis _axis;

        /// <summary>The beat number the current sweep was launched by.</summary>
        private int _lastBeatCount;

        /// <summary>The effect time at which the current sweep began.</summary>
        private double _sweepStartSeconds;

        /// <summary>Whether the current sweep is filling rather than clearing.</summary>
        private bool _sweepIsFilling = true;

        /// <summary>Whether any sweep has been launched yet.</summary>
        private bool _hasSwept;

        /// <summary>
        /// Creates one of the two versions.
        /// </summary>
        public FillAndClearEffect(FillAxis axis)
        {
            _axis = axis;
        }

        /// <inheritdoc />
        public string DisplayName =>
            _axis == FillAxis.Rows ? "Fill Horizontal" : "Fill Vertical";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        /// <inheritdoc />
        /// <remarks>
        /// Two of them, which is what the Flags attribute on EffectControl is
        /// there for: the beat source it shares with the other beat-driven
        /// effects, and the pacing that belongs to this one alone.
        /// </remarks>
        public EffectControl Controls =>
            EffectControl.BeatSource | EffectControl.FillPacing;

        /// <inheritdoc />
        public string Description =>
            _axis == FillAxis.Rows
                ? "Horizontal bars fill the wall outward from the middle row, then " +
                  "empty the same way. Use Fill and clear to choose whether a beat " +
                  "moves it one step or runs a whole sweep. Start audio capture to " +
                  "make it listen."
                : "Vertical bars fill the wall outward from the middle column, then " +
                  "empty the same way. Use Fill and clear to choose whether a beat " +
                  "moves it one step or runs a whole sweep. Start audio capture to " +
                  "make it listen.";

        /// <summary>
        /// How many bars there are to work through - rows or columns, depending
        /// on which version this is.
        /// </summary>
        private int BarCount => _axis == FillAxis.Rows ? WallFrame.Rows : WallFrame.Columns;

        /// <summary>
        /// How many steps it takes to get from the middle out to the edge,
        /// counting the middle itself as the first.
        ///
        /// Adding one before halving is what makes an odd-sized wall come out
        /// right: five bars have a middle and two steps beyond it, which is
        /// three positions rather than two.
        /// </summary>
        private int StepsFromMiddle => (BarCount + 1) / 2;

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            int stepsOneWay = StepsFromMiddle;

            bool filling;
            int reached;

            if (!context.IsAudioActive)
            {
                // Nothing to advance it, so it rests on the first picture - a
                // single lit bar across the middle - and holds still. That
                // matches what the other audio effects show while waiting, and
                // here it falls out naturally rather than being a special case.
                Forget();
                filling = true;
                reached = 0;
            }
            else if (context.Parameters.FillPacing == FillPacing.WholeSweepPerBeat)
            {
                AdvanceSweep(context, stepsOneWay, out filling, out reached);
            }
            else
            {
                // Forgotten so that switching pacing mid-track starts the next
                // sweep cleanly rather than resuming one abandoned earlier.
                Forget();

                // Which count this is - beats actually heard, or the tempo
                // metronome - is the user's choice. See BeatSource.
                int step = context.BeatCount % (stepsOneWay * 2);

                // The first half of the cycle fills, the second half empties.
                filling = step < stepsOneWay;
                reached = filling ? step : step - stepsOneWay;
            }

            for (int bar = 0; bar < BarCount; bar++)
            {
                int stepsOut = StepsOutFromMiddle(bar);

                // Filling lights everything the spread has reached. Emptying
                // darkens exactly the same set, which is what makes the hole
                // grow outward instead of the wall draining from the edges.
                bool lit = filling
                    ? stepsOut <= reached
                    : stepsOut > reached;

                if (lit)
                {
                    LightBar(target, bar);
                }
            }
        }

        /// <summary>
        /// Works out where a whole-sweep-per-beat run has got to.
        ///
        /// A beat launches a sweep rather than advancing the picture. The sweep
        /// then plays out on its own over the following fraction of a beat, and
        /// holds at whichever end it reached until the next beat launches the
        /// return trip. Even beats fill, odd beats clear.
        ///
        /// Same arrangement as Starburst, and it needs state for the same reason:
        /// a sweep is an EVENT rather than a position. When it started cannot be
        /// worked out from the current time alone, because it was decided at the
        /// moment a beat arrived - possibly many redraws ago.
        ///
        /// The repeatability rule still holds where it matters. A sweep is only
        /// launched when the beat COUNT changes, so drawing the same moment twice
        /// launches nothing the second time and gives the same picture.
        /// </summary>
        private void AdvanceSweep(
            EffectContext context, int stepsOneWay, out bool filling, out int reached)
        {
            double now = context.TimeSeconds;

            // Effect time restarts from zero when the effect is reselected, which
            // would leave a sweep that began at, say, 40 seconds looking as
            // though it starts in the future.
            if (now < _sweepStartSeconds)
            {
                Forget();
            }

            if (context.BeatCount != _lastBeatCount)
            {
                _lastBeatCount = context.BeatCount;
                _sweepStartSeconds = now;

                if (!_hasSwept)
                {
                    // THE FIRST SWEEP ALWAYS FILLS.
                    //
                    // The first attempt took the direction from whether the beat
                    // number was odd or even, on the reasoning that alternating
                    // from a remembered flag could drift while arithmetic on the
                    // count could not. Watching it play showed two faults with
                    // that.
                    //
                    // The wall rests showing a single middle bar, so if the first
                    // beat happened to land on an odd number it ran a CLEAR - and
                    // clearing assumes the wall is full. Instead of emptying it
                    // inverted, jumping from one lit bar to every bar but that
                    // one. Which of the two you got depended on nothing more than
                    // how many beats the track had played before the effect was
                    // selected.
                    //
                    // The drift the parity was protecting against also turned out
                    // to be the worse behaviour of the two. If two beats ever
                    // arrive between one frame and the next, the count jumps by
                    // two and the parity is unchanged - so the same direction
                    // runs twice, and the second run does nothing visible because
                    // the wall is already there. Alternating simply carries on.
                    _sweepIsFilling = true;
                    _hasSwept = true;
                }
                else
                {
                    _sweepIsFilling = !_sweepIsFilling;
                }
            }

            if (!_hasSwept)
            {
                // Listening, but nothing has been heard yet.
                filling = true;
                reached = 0;
                return;
            }

            double beatSeconds = AssumedBeatSeconds;

            if (context.Audio.TempoBpm > 0.0)
            {
                beatSeconds = 60.0 / context.Audio.TempoBpm;
            }

            double sweepSeconds = Math.Clamp(
                beatSeconds * SweepShareOfBeat,
                MinimumSweepSeconds,
                MaximumSweepSeconds);

            double throughSweep = Math.Clamp((now - _sweepStartSeconds) / sweepSeconds, 0.0, 1.0);

            filling = _sweepIsFilling;

            // Spread the pictures evenly across the sweep. The last one is held
            // rather than passed through, which is what leaves the wall full (or
            // dark) waiting for the next beat.
            reached = Math.Min((int)(throughSweep * stepsOneWay), stepsOneWay - 1);
        }

        /// <summary>
        /// Throws away any sweep in progress.
        /// </summary>
        private void Forget()
        {
            _lastBeatCount = 0;
            _sweepStartSeconds = 0.0;
            _sweepIsFilling = true;
            _hasSwept = false;
        }

        /// <summary>
        /// How many steps out from the middle a given bar sits.
        ///
        /// Written to work whether the wall has an odd or an even number of bars.
        /// With an odd count the middle is a single bar at a whole position, and
        /// distances come out whole. With an even count the middle falls between
        /// two bars, and cutting off the decimal puts both of those innermost
        /// bars at step zero - which is the sensible reading, since neither is
        /// more central than the other.
        /// </summary>
        private int StepsOutFromMiddle(int bar)
        {
            double middle = (BarCount - 1) / 2.0;

            return (int)Math.Abs(bar - middle);
        }

        /// <summary>
        /// Lights one whole bar, across the wall or down it.
        /// </summary>
        private void LightBar(WallFrame target, int bar)
        {
            if (_axis == FillAxis.Rows)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    target.SetCell(bar, column, true);
                }

                return;
            }

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                target.SetCell(row, bar, true);
            }
        }
    }
}
