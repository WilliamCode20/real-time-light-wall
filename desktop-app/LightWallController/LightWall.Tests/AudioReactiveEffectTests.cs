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
    }
}
