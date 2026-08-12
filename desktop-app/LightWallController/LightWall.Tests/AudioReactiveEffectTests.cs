using System;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Engine;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for effects that react to music.
    ///
    /// These are only possible because audio arrives as a plain immutable
    /// snapshot rather than being read from a sound card inside the effect. A
    /// test can hand over any level it likes and check what the wall does, with
    /// nothing playing and no audio hardware involved.
    ///
    /// That was the point of putting AudioFeatures in Core and keeping WASAPI
    /// out on the far side of an interface.
    /// </summary>
    public class AudioReactiveEffectTests
    {
        /// <summary>
        /// Renders an effect with audio at a given level.
        /// </summary>
        private static WallFrame RenderWithAudio(
            IWallEffect effect,
            double level,
            double timeSeconds = 0.0,
            bool isAudioActive = true,
            double[]? bandLevels = null)
        {
            // By default every band gets the same level, which stands in for
            // broadband sound - a wash of noise rather than a specific
            // instrument. Tests wanting one band to differ pass their own array.
            double[] bands = bandLevels ?? CreateBands(level);

            var features = new AudioFeatures(
                rms: level,
                peak: level,
                level: level,
                normalisedLevel: level,
                bandLevels: bands,
                isSilent: level <= 0.0);
            var context = new EffectContext(
                timeSeconds,
                new EffectParameters(),
                sessionSeed: 7,
                features,
                isAudioActive);

            var frame = new WallFrame();
            effect.Render(context, frame);
            return frame;
        }

        /// <summary>
        /// Builds a set of band levels all at the same value.
        /// </summary>
        private static double[] CreateBands(double level)
        {
            var bands = new double[FrequencyBands.Count];

            for (int i = 0; i < bands.Length; i++)
            {
                bands[i] = level;
            }

            return bands;
        }

        // ------------------------------------------------------------------
        // EQ Bumper following the music
        // ------------------------------------------------------------------

        [Fact]
        public void EqBumper_GoesDarkWhenTheMusicStops()
        {
            // With capture running and nothing playing, a dark wall is the
            // honest answer. This is the case the fallback must NOT apply to.
            var effect = new EqBumperEffect();

            WallFrame frame = RenderWithAudio(effect, level: 0.0);

            Assert.Equal(0, frame.CountLitCells());
        }

        [Fact]
        public void EqBumper_FillsTheWallWhenTheMusicIsLoud()
        {
            var effect = new EqBumperEffect();

            WallFrame frame = RenderWithAudio(effect, level: 1.0);

            // Every column should be at or near full height.
            Assert.True(
                frame.CountLitCells() >= 28,
                $"Only {frame.CountLitCells()} bulbs lit at full level; expected nearly all 35.");
        }

        [Fact]
        public void EqBumper_GetsTallerAsTheMusicGetsLouder()
        {
            // The behaviour that matters most: more sound, more light.
            var effect = new EqBumperEffect();

            int quiet = RenderWithAudio(effect, level: 0.2).CountLitCells();
            int medium = RenderWithAudio(effect, level: 0.5).CountLitCells();
            int loud = RenderWithAudio(effect, level: 0.9).CountLitCells();

            Assert.True(quiet < medium, $"Quiet lit {quiet}, medium lit {medium}.");
            Assert.True(medium < loud, $"Medium lit {medium}, loud lit {loud}.");
        }

        [Fact]
        public void EqBumper_BarsStaySolidFromTheBottomWhenFollowingAudio()
        {
            // A bar has to be a continuous run rising from the bottom. A gap
            // would mean cells floating in mid-air, which is not what an
            // equaliser bar looks like.
            var effect = new EqBumperEffect();

            for (double level = 0.0; level <= 1.0; level += 0.1)
            {
                for (double time = 0.0; time < 1.0; time += 0.25)
                {
                    WallFrame frame = RenderWithAudio(effect, level, time);

                    for (int column = 0; column < WallFrame.Columns; column++)
                    {
                        bool foundGap = false;

                        for (int row = WallFrame.Rows - 1; row >= 0; row--)
                        {
                            if (!frame.GetCell(row, column))
                            {
                                foundGap = true;
                            }
                            else if (foundGap)
                            {
                                Assert.Fail(
                                    $"Column {column} had a floating lit cell above a gap " +
                                    $"at level {level:F1}, time {time:F2}.");
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void EqBumper_GivesEachColumnItsOwnFrequencyBand()
        {
            // The whole point of the frequency split. A sound with energy only
            // in the low end should light the left of the wall and leave the
            // right dark - a kick drum, not a wash of noise.
            var effect = new EqBumperEffect();

            var bassOnly = new double[FrequencyBands.Count];
            bassOnly[0] = 1.0;
            bassOnly[1] = 0.8;

            WallFrame frame = RenderWithAudio(effect, level: 0.5, bandLevels: bassOnly);

            Assert.Equal(WallFrame.Rows, ColumnHeight(frame, 0));
            Assert.Equal(4, ColumnHeight(frame, 1));

            // Everything above the bass stays dark.
            for (int column = 2; column < WallFrame.Columns; column++)
            {
                Assert.Equal(0, ColumnHeight(frame, column));
            }
        }

        [Fact]
        public void EqBumper_PutsLowFrequenciesOnTheLeft()
        {
            // Reading left to right as low to high is the convention every
            // equaliser display uses, and getting it backwards would look
            // subtly wrong to anyone who has seen one.
            var effect = new EqBumperEffect();

            var trebleOnly = new double[FrequencyBands.Count];
            trebleOnly[FrequencyBands.Count - 1] = 1.0;

            WallFrame frame = RenderWithAudio(effect, level: 0.5, bandLevels: trebleOnly);

            Assert.Equal(0, ColumnHeight(frame, 0));
            Assert.Equal(WallFrame.Rows, ColumnHeight(frame, WallFrame.Columns - 1));
        }

        /// <summary>
        /// Counts how many rows are lit in one column.
        /// </summary>
        private static int ColumnHeight(WallFrame frame, int column)
        {
            int height = 0;

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                if (frame.GetCell(row, column))
                {
                    height++;
                }
            }

            return height;
        }

        [Fact]
        public void EqBumper_UsesTheWholeHeightOfTheWall()
        {
            // The complaint that prompted the rework: the bars barely moved,
            // reaching one or two rows at most. Full scale must reach the top.
            var effect = new EqBumperEffect();

            int atFullScale = CountColumnHeight(RenderWithAudio(effect, level: 1.0));
            int atThreeQuarters = CountColumnHeight(RenderWithAudio(effect, level: 0.75));
            int atHalf = CountColumnHeight(RenderWithAudio(effect, level: 0.5));

            Assert.Equal(WallFrame.Rows, atFullScale);
            Assert.Equal(4, atThreeQuarters);
            Assert.Equal(3, atHalf);
        }

        [Fact]
        public void EqBumper_ShowsOneRowWhenNothingIsListening()
        {
            // Nobody has started audio capture. A single lit row says "running,
            // waiting for sound" without inventing motion that might be mistaken
            // for a response to music.
            var effect = new EqBumperEffect();

            WallFrame frame = RenderWithAudio(effect, level: 0.0, isAudioActive: false);

            Assert.Equal(WallFrame.Columns, frame.CountLitCells());

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                Assert.True(frame.GetCell(WallFrame.Rows - 1, column));
            }
        }

        [Fact]
        public void EqBumper_DoesNotMoveOnItsOwnWhileListening()
        {
            // The bug that prompted this rework. A travelling sine wave used to
            // roll peaks and troughs across the wall regardless of the music,
            // which made it impossible to tell whether the wall was really
            // following the sound.
            //
            // With a steady level, the picture must be completely still.
            var effect = new EqBumperEffect();

            WallFrame atStart = RenderWithAudio(effect, level: 0.6, timeSeconds: 0.0);

            for (double time = 0.1; time <= 5.0; time += 0.1)
            {
                WallFrame later = RenderWithAudio(effect, level: 0.6, timeSeconds: time);

                Assert.True(
                    later.ContentEquals(atStart),
                    $"The wall changed at {time:F1}s despite the level being constant.");
            }
        }

        /// <summary>
        /// Counts how many rows are lit in the first column. Since all columns
        /// move together, one is representative.
        /// </summary>
        private static int CountColumnHeight(WallFrame frame)
        {
            int height = 0;

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                if (frame.GetCell(row, 0))
                {
                    height++;
                }
            }

            return height;
        }

        [Fact]
        public void EqBumper_TellsApartNotListeningFromListeningToSilence()
        {
            // The distinction the IsAudioActive flag exists for. Both have a
            // level of zero; they should not look the same.
            var effect = new EqBumperEffect();

            WallFrame notListening = RenderWithAudio(effect, level: 0.0, isAudioActive: false);
            WallFrame listeningToSilence = RenderWithAudio(effect, level: 0.0, isAudioActive: true);

            Assert.True(notListening.CountLitCells() > 0);
            Assert.Equal(0, listeningToSilence.CountLitCells());
        }

        [Fact]
        public void EqBumper_IsStillRepeatableWithAudio()
        {
            // The rule every effect follows: the same moment must produce the
            // same picture. Adding audio must not have broken it.
            var effect = new EqBumperEffect();

            WallFrame first = RenderWithAudio(effect, level: 0.55, timeSeconds: 1.25);
            WallFrame second = RenderWithAudio(effect, level: 0.55, timeSeconds: 1.25);

            Assert.True(first.ContentEquals(second));
        }

        // ------------------------------------------------------------------
        // Audio reaching effects through the engine
        // ------------------------------------------------------------------

        [Fact]
        public void TheEngineHandsAudioThroughToEffects()
        {
            var engine = new WallEngine();
            var effect = new EqBumperEffect();

            engine.IsAudioActive = true;
            engine.CurrentAudio = new AudioFeatures(0.0, 0.0, 0.0, 0.0, CreateBands(0.0), isSilent: true);
            engine.Play(effect);

            Assert.Equal(0, engine.CurrentFrame.CountLitCells());

            // Turn the music up and the wall should respond on the next frame.
            engine.CurrentAudio = new AudioFeatures(0.8, 0.9, 0.9, 0.9, CreateBands(0.9), isSilent: false);
            engine.Advance(0.01);

            Assert.True(
                engine.CurrentFrame.CountLitCells() > 0,
                "Raising the audio level did not light the wall.");
        }

        [Fact]
        public void AnEngineWithNoAudioSetIsSimplyNotListening()
        {
            // The default has to be safe: an engine nobody has told about audio
            // should behave exactly as it did before audio existed.
            var engine = new WallEngine();

            Assert.False(engine.IsAudioActive);
            Assert.True(engine.CurrentAudio.IsSilent);
            Assert.Equal(0.0, engine.CurrentAudio.Level);
        }

        /// <summary>
        /// A stand-in audio source, so the clock can be tested with no sound.
        /// </summary>
        private sealed class FakeAudioSource : IAudioSource
        {
            public string Name => "Fake";

            public bool IsRunning { get; set; }

            public AudioFeatures CurrentFeatures { get; set; } = AudioFeatures.Silence;

            public string? LastError => null;

            public void Start() => IsRunning = true;

            public void Stop() => IsRunning = false;

            public void Dispose() { }
        }

        [Fact]
        public void TheClockPassesAudioFromItsSourceIntoTheEngine()
        {
            using var clock = new WallShowClock();

            var source = new FakeAudioSource
            {
                IsRunning = true,
                CurrentFeatures = new AudioFeatures(0.7, 0.8, 0.85, 0.85, CreateBands(0.85), isSilent: false)
            };

            clock.AudioSource = source;
            clock.Modify(engine => engine.Play(new EqBumperEffect()));

            // One manual tick, standing in for the background thread.
            clock.AdvanceManually(0.01);

            var frame = new WallFrame();
            clock.CopyCurrentFrameTo(frame);

            Assert.True(
                frame.CountLitCells() > 0,
                "The clock did not pass the audio level through to the effect.");
        }

        [Fact]
        public void TheClockCopesWithNoAudioSourceAttached()
        {
            // Nothing should fall over just because nobody wired up audio.
            using var clock = new WallShowClock();

            clock.Modify(engine => engine.Play(new EqBumperEffect()));
            clock.AdvanceManually(0.01);

            var frame = new WallFrame();
            clock.CopyCurrentFrameTo(frame);

            // Falls back to the test pattern, so the wall is not dark.
            Assert.True(frame.CountLitCells() > 0);
        }

        // ------------------------------------------------------------------
        // Starburst
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds band levels with the low end and the high end set separately,
        /// which is how Starburst decides what kind of burst to throw.
        /// </summary>
        private static double[] CreateSplitBands(double bass, double treble)
        {
            var bands = new double[FrequencyBands.Count];

            bands[0] = bass;
            bands[1] = bass;
            bands[2] = 0.0;
            bands[3] = 0.0;
            bands[4] = treble;
            bands[5] = treble;
            bands[6] = treble;

            return bands;
        }

        /// <summary>
        /// Renders one whole burst from the beat that starts it until the wall
        /// goes dark again, and reports what was seen along the way.
        ///
        /// Sampling rather than checking one moment, because a burst is a
        /// travelling ring - any single frame catches it at one radius and says
        /// almost nothing about the whole explosion.
        /// </summary>
        private static (int mostLitAtOnce, int framesWithAnythingLit, int finalLit) PlayOneBurst(
            StarburstEffect effect,
            double bass,
            double treble,
            int sessionSeed,
            int beatCount = 1,
            double tempoBpm = 120.0)
        {
            var features = new AudioFeatures(
                rms: 0.5,
                peak: 0.5,
                level: 0.5,
                normalisedLevel: 0.5,
                bandLevels: CreateSplitBands(bass, treble),
                isSilent: false,
                secondsSinceBeat: 0.0,
                beatCount: beatCount,
                tempoBpm: tempoBpm,
                tempoConfidence: 1.0);

            var frame = new WallFrame();

            int mostLitAtOnce = 0;
            int framesWithAnythingLit = 0;
            int finalLit = 0;

            // A beat at 120 BPM is half a second, so this covers one whole gap
            // between beats in twenty-five steps.
            for (int step = 0; step <= 25; step++)
            {
                var context = new EffectContext(
                    step * 0.02,
                    new EffectParameters(),
                    sessionSeed,
                    features,
                    isAudioActive: true);

                effect.Render(context, frame);

                int lit = frame.CountLitCells();

                if (lit > mostLitAtOnce)
                {
                    mostLitAtOnce = lit;
                }

                if (lit > 0)
                {
                    framesWithAnythingLit++;
                }

                finalLit = lit;
            }

            return (mostLitAtOnce, framesWithAnythingLit, finalLit);
        }

        [Fact]
        public void Starburst_ShowsNothingUntilABeatArrives()
        {
            // Listening, but the music has not produced a beat yet. A dark wall
            // is the honest answer - the same distinction EQ Bumper makes
            // between "nobody is listening" and "listening to nothing".
            var effect = new StarburstEffect();

            WallFrame frame = RenderWithAudio(effect, level: 0.5, timeSeconds: 0.0);

            Assert.Equal(0, frame.CountLitCells());
        }

        [Fact]
        public void Starburst_ShowsAStillPatternWhenNobodyIsListening()
        {
            // Running and waiting, without inventing motion that could be
            // mistaken for a response to sound.
            var effect = new StarburstEffect();

            WallFrame early = RenderWithAudio(
                effect, level: 0.0, timeSeconds: 0.0, isAudioActive: false);
            WallFrame later = RenderWithAudio(
                effect, level: 0.0, timeSeconds: 3.7, isAudioActive: false);

            Assert.Equal(5, early.CountLitCells());
            Assert.True(
                early.ContentEquals(later),
                "The waiting pattern moved, which makes it look like it heard something.");
        }

        [Fact]
        public void Starburst_StartsFromASingleBulbAndSpreadsOut()
        {
            var effect = new StarburstEffect();

            var features = new AudioFeatures(
                rms: 0.5, peak: 0.5, level: 0.5, normalisedLevel: 0.5,
                bandLevels: CreateSplitBands(bass: 0.9, treble: 0.0),
                isSilent: false,
                secondsSinceBeat: 0.0,
                beatCount: 1,
                tempoBpm: 120.0,
                tempoConfidence: 1.0);

            var frame = new WallFrame();

            void RenderAtTime(double seconds)
            {
                effect.Render(
                    new EffectContext(
                        seconds, new EffectParameters(), 7, features, isAudioActive: true),
                    frame);
            }

            // The instant the beat lands, only the middle of the burst is lit.
            RenderAtTime(0.0);
            Assert.Equal(1, frame.CountLitCells());

            // Shortly after, the ring has moved outward and taken more with it.
            RenderAtTime(0.1);
            Assert.True(
                frame.CountLitCells() > 1,
                "The burst never spread beyond its middle bulb.");
        }

        [Fact]
        public void Starburst_IsGoneBeforeTheNextBeatIsDue()
        {
            // The wall must read as separate explosions rather than one
            // continuous churn, which means each burst has to finish inside the
            // gap between beats.
            var effect = new StarburstEffect();

            (int mostLit, int framesLit, int finalLit) =
                PlayOneBurst(effect, bass: 0.9, treble: 0.0, sessionSeed: 7);

            Assert.True(mostLit > 0, "The burst never appeared at all.");
            Assert.Equal(0, finalLit);

            // And it should have been gone for a little while by then, not just
            // scraping in on the final frame.
            Assert.True(
                framesLit < 24,
                $"The burst was still lit on {framesLit} of 26 frames, leaving no gap before the next beat.");
        }

        [Fact]
        public void Starburst_ThrowsABiggerBurstForHeavierBass()
        {
            // Summed across many placements rather than judged from one, because
            // a burst in a corner has most of itself off the wall. Comparing a
            // single big burst against a single small one could be comparing a
            // clipped corner against a centred one and prove nothing.
            int heavyTotal = 0;
            int lightTotal = 0;

            for (int seed = 0; seed < 40; seed++)
            {
                heavyTotal += PlayOneBurst(
                    new StarburstEffect(), bass: 0.95, treble: 0.0, sessionSeed: seed).mostLitAtOnce;

                lightTotal += PlayOneBurst(
                    new StarburstEffect(), bass: 0.10, treble: 0.0, sessionSeed: seed).mostLitAtOnce;
            }

            Assert.True(
                heavyTotal > lightTotal,
                $"Heavy bass produced {heavyTotal} lit bulbs across 40 placements " +
                $"against {lightTotal} for light bass - the low end is not driving the size.");
        }

        [Fact]
        public void Starburst_DrawsADifferentShapeWhenTheTopEndLeads()
        {
            // Bass is held high in both cases, so both bursts are the same size
            // and any difference in the picture is the shape changing rather
            // than the radius.
            int bassTotal = 0;
            int trebleTotal = 0;

            for (int seed = 0; seed < 40; seed++)
            {
                bassTotal += PlayOneBurst(
                    new StarburstEffect(), bass: 0.9, treble: 0.1, sessionSeed: seed).mostLitAtOnce;

                trebleTotal += PlayOneBurst(
                    new StarburstEffect(), bass: 0.9, treble: 0.95, sessionSeed: seed).mostLitAtOnce;
            }

            // Spokes are eight thin arms where the diamond is a solid ring, so
            // the same radius lights noticeably fewer bulbs.
            Assert.True(
                trebleTotal < bassTotal,
                $"A treble-led beat lit {trebleTotal} bulbs against {bassTotal} for a bass-led one " +
                "of the same size - the shape is not changing.");
        }

        /// <summary>
        /// Renders one frame of a burst with the centre forced to a known place,
        /// by hunting for a session seed that puts it there.
        ///
        /// Placement is random on purpose, so a test that needs to know the exact
        /// picture has to find a seed that lands where it wants rather than
        /// asking the effect to put it there. Adding a "place it here" setting
        /// only tests would use would be worse - it would mean the thing under
        /// test was not quite the thing that runs.
        /// </summary>
        private static WallFrame? RenderBurstCentredAt(
            int centreRow,
            int centreColumn,
            double bass,
            double treble,
            double atSeconds)
        {
            var features = new AudioFeatures(
                0.5, 0.5, 0.5, 0.5, CreateSplitBands(bass, treble),
                isSilent: false, secondsSinceBeat: 0.0, beatCount: 1,
                tempoBpm: 120.0, tempoConfidence: 1.0);

            for (int seed = 0; seed < 400; seed++)
            {
                var effect = new StarburstEffect();
                var frame = new WallFrame();

                // The first frame of any burst is its middle bulb alone, so this
                // finds where the burst landed.
                effect.Render(
                    new EffectContext(0.0, new EffectParameters(), seed, features, true),
                    frame);

                if (frame.CountLitCells() != 1 || !frame.GetCell(centreRow, centreColumn))
                {
                    continue;
                }

                effect.Render(
                    new EffectContext(atSeconds, new EffectParameters(), seed, features, true),
                    frame);

                return frame;
            }

            return null;
        }

        [Fact]
        public void Starburst_NeverDrawsTheThreeByThreeRing()
        {
            // The shape that had to go. Eight arms all the same distance out
            // means, one step from the middle, the eight bulbs surrounding it -
            // a filled 3x3 square with the middle off, which is not a star by
            // any reading. Small bursts used to end on it.
            //
            // Checked across both kinds, every size and the whole life of the
            // burst, because it appeared as a final frame rather than as a
            // deliberate step.
            foreach (double bass in new[] { 0.10, 0.50, 0.95 })
            {
                foreach (double treble in new[] { 0.0, 0.99 })
                {
                    for (int seed = 0; seed < 30; seed++)
                    {
                        var effect = new StarburstEffect();
                        var features = new AudioFeatures(
                            0.5, 0.5, 0.5, 0.5, CreateSplitBands(bass, treble),
                            isSilent: false, secondsSinceBeat: 0.0, beatCount: 1,
                            tempoBpm: 120.0, tempoConfidence: 1.0);

                        var frame = new WallFrame();

                        for (int step = 0; step <= 25; step++)
                        {
                            effect.Render(
                                new EffectContext(
                                    step * 0.02, new EffectParameters(), seed, features, true),
                                frame);

                            AssertNoThreeByThreeRing(frame, bass, treble, seed, step);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fails if any bulb has all eight of its neighbours lit while it is not.
        /// </summary>
        private static void AssertNoThreeByThreeRing(
            WallFrame frame, double bass, double treble, int seed, int step)
        {
            for (int row = 1; row < WallFrame.Rows - 1; row++)
            {
                for (int column = 1; column < WallFrame.Columns - 1; column++)
                {
                    if (frame.GetCell(row, column))
                    {
                        continue;
                    }

                    bool allEightLit = true;

                    for (int dr = -1; dr <= 1 && allEightLit; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0)
                            {
                                continue;
                            }

                            if (!frame.GetCell(row + dr, column + dc))
                            {
                                allEightLit = false;
                                break;
                            }
                        }
                    }

                    Assert.False(
                        allEightLit,
                        $"A hollow 3x3 square appeared at ({row},{column}) with bass {bass}, " +
                        $"treble {treble}, seed {seed}, step {step}.");
                }
            }
        }

        [Fact]
        public void Starburst_PointsAlongTheAxesForBassAndTheDiagonalsForTreble()
        {
            // The smallest form of each kind, which is where they have to be most
            // clearly different - a plus for one and an X for the other.
            //
            // Centred well inside the wall so nothing is clipped and the whole
            // shape can be checked.
            WallFrame? bassLed = RenderBurstCentredAt(2, 3, bass: 0.10, treble: 0.0, atSeconds: 0.06);
            WallFrame? trebleLed = RenderBurstCentredAt(2, 3, bass: 0.10, treble: 0.99, atSeconds: 0.06);

            Assert.NotNull(bassLed);
            Assert.NotNull(trebleLed);

            // A plus: straight neighbours lit, corners dark.
            Assert.True(bassLed!.GetCell(1, 3), "Bass-led burst is missing its upward point.");
            Assert.True(bassLed.GetCell(3, 3), "Bass-led burst is missing its downward point.");
            Assert.True(bassLed.GetCell(2, 2), "Bass-led burst is missing its left point.");
            Assert.True(bassLed.GetCell(2, 4), "Bass-led burst is missing its right point.");
            Assert.False(bassLed.GetCell(1, 2), "Bass-led burst lit a corner, so it is not a plus.");
            Assert.False(bassLed.GetCell(3, 4), "Bass-led burst lit a corner, so it is not a plus.");

            // An X: corners lit, straight neighbours dark.
            Assert.True(trebleLed!.GetCell(1, 2), "Treble-led burst is missing a corner point.");
            Assert.True(trebleLed.GetCell(1, 4), "Treble-led burst is missing a corner point.");
            Assert.True(trebleLed.GetCell(3, 2), "Treble-led burst is missing a corner point.");
            Assert.True(trebleLed.GetCell(3, 4), "Treble-led burst is missing a corner point.");
            Assert.False(trebleLed.GetCell(1, 3), "Treble-led burst lit a straight arm, so it is not an X.");
            Assert.False(trebleLed.GetCell(2, 2), "Treble-led burst lit a straight arm, so it is not an X.");
        }

        [Fact]
        public void Starburst_HasPointsThatReachFurtherThanItsSides()
        {
            // What makes it a star rather than a diamond or a square: at full
            // stretch the leading arms are further out than the trailing ones,
            // so the sides fall inward instead of running straight between the
            // points.
            WallFrame? big = RenderBurstCentredAt(2, 3, bass: 0.95, treble: 0.0, atSeconds: 0.36);

            Assert.NotNull(big);

            // Left and right points three steps out.
            Assert.True(big!.GetCell(2, 0), "The left point did not reach three steps out.");
            Assert.True(big.GetCell(2, 6), "The right point did not reach three steps out.");

            // The diagonal arms are held one step back, so the bulbs on the
            // straight line between two points must be dark. A diamond would
            // have lit these.
            Assert.False(big.GetCell(1, 1), "A bulb on the straight edge is lit, so this is a diamond.");
            Assert.False(big.GetCell(3, 5), "A bulb on the straight edge is lit, so this is a diamond.");
        }

        // ------------------------------------------------------------------
        // Choosing where the beat comes from
        // ------------------------------------------------------------------

        /// <summary>
        /// Renders Starburst with the two beat counters set to different values,
        /// so it is obvious which one was followed.
        /// </summary>
        private static int LitCellsFollowing(
            BeatSource source, int detectedBeats, int tempoPulses)
        {
            var features = new AudioFeatures(
                0.5, 0.5, 0.5, 0.5, CreateSplitBands(0.9, 0.0),
                isSilent: false,
                secondsSinceBeat: 0.0,
                beatCount: detectedBeats,
                tempoBpm: 120.0,
                tempoConfidence: 1.0,
                secondsSincePulse: 0.0,
                pulseCount: tempoPulses);

            var parameters = new EffectParameters { BeatSource = source };
            var frame = new WallFrame();

            new StarburstEffect().Render(
                new EffectContext(0.0, parameters, 7, features, true), frame);

            return frame.CountLitCells();
        }

        [Fact]
        public void BeatSource_DecidesWhichCounterAnEffectFollows()
        {
            // Only one of the two counters has moved in each case, so a burst
            // appearing proves which one was read.
            Assert.Equal(1, LitCellsFollowing(BeatSource.Detected, detectedBeats: 4, tempoPulses: 0));
            Assert.Equal(0, LitCellsFollowing(BeatSource.Detected, detectedBeats: 0, tempoPulses: 4));

            Assert.Equal(1, LitCellsFollowing(BeatSource.Tempo, detectedBeats: 0, tempoPulses: 4));
            Assert.Equal(0, LitCellsFollowing(BeatSource.Tempo, detectedBeats: 4, tempoPulses: 0));
        }

        [Fact]
        public void BeatSource_IsCarriedThroughWhenParametersAreCopied()
        {
            // Parameters get cloned whenever something needs a snapshot that will
            // not change underneath it. A setting missing from Clone silently
            // reverts to its default, which is the kind of fault that shows up
            // as "it sometimes ignores the switch".
            var original = new EffectParameters { BeatSource = BeatSource.Tempo };

            Assert.Equal(BeatSource.Tempo, original.Clone().BeatSource);
        }

        [Fact]
        public void BeatFlashAndTempoPulse_IgnoreTheBeatSourceSetting()
        {
            // These two exist to show the difference between what was heard and
            // what was predicted. Letting either follow the switch would remove
            // the only honest way to judge whether detection is working, so both
            // must stay pinned to their own source.
            // A beat has just been heard, but the metronome is half way between
            // its own beats.
            //
            // Tempo Pulse works from BeatPhase rather than from the time since
            // the last pulse, so the phase is what has to say "not now" - a
            // first attempt at this test left it at its default of 0, which
            // means "exactly on the beat", and Tempo Pulse quite correctly lit
            // the whole wall.
            var heardOnly = new AudioFeatures(
                0.5, 0.5, 0.5, 0.5, CreateBands(0.5),
                isSilent: false,
                secondsSinceBeat: 0.0,
                beatCount: 1,
                tempoBpm: 120.0,
                tempoConfidence: 1.0,
                secondsSincePulse: AudioFeatures.NoBeatYet,
                pulseCount: 0,
                beatPhase: 0.6);

            var followTempo = new EffectParameters { BeatSource = BeatSource.Tempo };
            var frame = new WallFrame();

            // A beat was heard but the metronome has not struck. Beat Flash must
            // still flash, because it always follows what was heard.
            new BeatFlashEffect().Render(
                new EffectContext(0.0, followTempo, 7, heardOnly, true), frame);

            Assert.Equal(35, frame.CountLitCells());

            // And Tempo Pulse must stay dark, because it always follows the
            // metronome - which has not struck.
            new TempoPulseEffect().Render(
                new EffectContext(0.0, followTempo, 7, heardOnly, true), frame);

            Assert.Equal(0, frame.CountLitCells());
        }

        [Fact]
        public void Starburst_SurvivesBurstsCentredAnywhereIncludingCorners()
        {
            // The reason this is worth a test of its own: WallFrame.SetCell
            // throws on a coordinate that is off the wall rather than ignoring
            // it. A burst centred in a corner has most of its rings out of
            // bounds, so anything that generated coordinates and then tried to
            // set them would fail here rather than simply drawing the part that
            // fits.
            int clippedBursts = 0;

            for (int seed = 0; seed < 120; seed++)
            {
                (int mostLit, _, _) = PlayOneBurst(
                    new StarburstEffect(), bass: 0.95, treble: 0.0, sessionSeed: seed);

                // A full-size burst centred well inside the wall lights far more
                // than this at its widest; a clipped one lights fewer.
                if (mostLit < 10)
                {
                    clippedBursts++;
                }
            }

            // Placement is random, so some of those 120 must have landed near an
            // edge. If none did, the test is not exercising what it claims to.
            Assert.True(
                clippedBursts > 0,
                "No burst landed near an edge, so clipping was never actually exercised.");
        }
    }
}
