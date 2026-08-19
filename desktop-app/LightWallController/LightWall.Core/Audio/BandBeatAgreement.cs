using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Works out which frequency bands are actually carrying the beat, so the
    /// ones that are not can be leaned on less.
    ///
    /// WHY THIS EXISTS - MEASURED, NOT ASSUMED
    ///
    /// Onset detection adds all seven bands together and looks for a jump in the
    /// total. That treats a kick drum and a string pad as equally good evidence,
    /// and on real music they are nothing like it.
    ///
    /// Recording four real tracks and measuring how tightly each band's energy
    /// clusters at one point in the beat - 1.00 would be every onset at exactly
    /// the same moment of every beat, 0.00 would be no relationship at all:
    ///
    ///   track                   band 0   band 1   bands 3-6   the SUM used
    ///   She Wants Me To Be...     0.98     0.55    0.19-0.28      0.37
    ///   Me Too                    0.94     0.93    0.22-0.68      0.87
    ///   Classic Pursuit           0.56     0.57    0.11-0.28      0.54
    ///   Time Of Our Lives         0.35     0.22    0.07-0.18      0.16
    ///
    /// The first row is the whole argument. The beat is sitting in the sub band
    /// in near-perfect condition, and adding six bands of unrelated content on
    /// top drags what the detector actually sees down to 0.37. The upper bands
    /// are worse than useless: they produce the HIGHEST onset rates of any band
    /// with almost no relationship to the beat, so they do not merely dilute the
    /// signal, they actively bury it.
    ///
    /// WHAT IS MEASURED, AND WHY NOT THE OBVIOUS THING
    ///
    /// The obvious question is "does this band have a consistent rhythm of its
    /// own". That is the wrong question and it fails in a specific way: a
    /// syncopated melody line is perfectly consistent and completely misleading.
    /// Following it would lock the wall onto something that is not the beat.
    ///
    /// The question asked here is "does this band agree with the beat we already
    /// think we have". That uses the tempo as a prior rather than as something
    /// to be rediscovered per band, which is both cheaper and far safer.
    ///
    /// HOW AGREEMENT IS MEASURED
    ///
    /// Each band's flux is added up as though it were pointing round a circle,
    /// where one full turn is one beat. Energy that always arrives at the same
    /// point in the beat piles up in one direction and the total is long; energy
    /// scattered evenly through the beat points every way at once and cancels to
    /// nearly nothing. The length of that total, against the energy that went
    /// into it, is the agreement.
    ///
    /// This is a standard way of averaging angles, and it is the only sensible
    /// one - ordinary averaging says that something landing just before the beat
    /// and something landing just after average out to half a beat away, which
    /// is the opposite of the truth.
    ///
    /// WHY IT ALSO LOOKS AT TWICE THE BEAT RATE
    ///
    /// Because otherwise it would punish the very band it exists to find.
    ///
    /// If the tempo estimate has settled at half the true speed - which is the
    /// characteristic failure of an under-fed detector - then a kick landing on
    /// every real beat arrives at the top of the circle and the bottom of it
    /// alternately. Those two cancel exactly, so the band carrying the beat
    /// perfectly would score ZERO and be weighted down to nothing, while a band
    /// lazy enough to hit only every other beat would score 1.00 and be promoted.
    /// The weighting would drive itself deeper into the error.
    ///
    /// Measuring at twice the rate as well, and taking whichever is stronger,
    /// removes that. Something landing on every beat and something landing on
    /// every half beat are both locked to the grid, and both are useful evidence.
    /// What still scores low is content with no rhythmic relationship at all -
    /// which is exactly what should be weighted down.
    ///
    /// WHY EVERY BAND KEEPS A SHARE
    ///
    /// No band is ever silenced. See SmallestShare: a hard switch has to pick a
    /// moment to hand over, and that moment is where phase lurches come from. It
    /// also has to be able to hand back - a band that goes quiet during a break
    /// and returns needs to be audible enough to earn its weight again, and a
    /// band weighted to zero can never demonstrate anything.
    /// </summary>
    public sealed class BandBeatAgreement
    {
        /// <summary>Running total of each band's flux, pointing round the circle.</summary>
        private readonly double[] _acrossBeat;
        private readonly double[] _alongBeat;

        /// <summary>The same again at twice the beat rate. See the class notes.</summary>
        private readonly double[] _acrossHalfBeat;
        private readonly double[] _alongHalfBeat;

        /// <summary>How much flux went into those totals, for scale.</summary>
        private readonly double[] _totalFlux;

        /// <summary>What each band is currently worth, averaging 1.</summary>
        private readonly double[] _weights;

        /// <summary>How well each band agrees, from 0 to 1. Kept for diagnostics.</summary>
        private readonly double[] _agreement;

        /// <summary>
        /// Creates an agreement tracker for the usual number of bands.
        /// </summary>
        public BandBeatAgreement()
        {
            int bands = FrequencyBands.Count;

            _acrossBeat = new double[bands];
            _alongBeat = new double[bands];
            _acrossHalfBeat = new double[bands];
            _alongHalfBeat = new double[bands];
            _totalFlux = new double[bands];
            _weights = new double[bands];
            _agreement = new double[bands];

            Forget();
        }

        /// <summary>
        /// How long the running totals take to forget, in seconds.
        ///
        /// Long enough that a bar or two of music decides the weights rather than
        /// a single hit, short enough that a section handing the beat from the
        /// drums to something else is followed within a few bars.
        ///
        /// Measured by sweeping it against eleven real recordings. Three seconds
        /// is clearly too twitchy - the share of time spent on the right tempo
        /// drops from 72% to 65% as the weights start chasing individual phrases.
        /// Six through twelve seconds are flat and equally good, so eight sits
        /// safely inside a broad region rather than balanced on a narrow one.
        /// </summary>
        public double MemorySeconds { get; set; } = 8.0;

        /// <summary>
        /// The least a band can be worth, as a share of an equal split.
        ///
        /// THIS IS WHAT MAKES IT A WEIGHTING RATHER THAN A SWITCH.
        ///
        /// A band scoring nothing still contributes a quarter of what it would
        /// under an equal split. That matters in both directions. A hard switch
        /// has to choose a moment to hand over and that moment is where phase
        /// lurches come from - and more practically, a band weighted to zero can
        /// never show that it has started carrying the beat, so the weighting
        /// could never hand back.
        /// </summary>
        public double SmallestShare { get; set; } = 0.25;

        /// <summary>
        /// True once enough has been heard for the weights to mean anything.
        ///
        /// Until then every band is worth the same, which is exactly the
        /// behaviour this replaced - so nothing is worse off while it learns.
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// How much flux must have been gathered before the weights are trusted.
        ///
        /// Guards against the opening moments of a track, where one hit in one
        /// band would otherwise be unanimous evidence that only that band
        /// matters.
        /// </summary>
        public double FluxNeededBeforeTrusting { get; set; } = 0.5;

        /// <summary>
        /// How well a band has been agreeing with the beat, from 0 to 1.
        /// Exposed for tests and for anything that wants to show it.
        /// </summary>
        public double GetAgreement(int band)
        {
            if (band < 0 || band >= _agreement.Length)
            {
                return 0.0;
            }

            return _agreement[band];
        }

        /// <summary>
        /// What a band's flux should be multiplied by. Averages 1 across the
        /// bands, so the overall size of the flux does not depend on how uneven
        /// the weighting happens to be.
        /// </summary>
        public double GetWeight(int band)
        {
            if (band < 0 || band >= _weights.Length)
            {
                return 1.0;
            }

            return _weights[band];
        }

        /// <summary>
        /// Takes in one reading.
        /// </summary>
        /// <param name="bandFlux">
        /// How much each band grew since the previous reading. Raw, not weighted
        /// - this is measuring what the bands did, not what we made of them.
        /// </param>
        /// <param name="beatPhase">
        /// How far through the current beat, from 0 to 1, from the metronome.
        /// </param>
        /// <param name="deltaSeconds">Time since the previous reading.</param>
        public void Observe(double[] bandFlux, double beatPhase, double deltaSeconds)
        {
            if (bandFlux is null)
            {
                throw new ArgumentNullException(nameof(bandFlux));
            }

            Fade(deltaSeconds);

            // One turn of the circle per beat, and a second turn twice as fast.
            // Worked out once here rather than per band, since the phase is the
            // same for all of them.
            double angle = 2.0 * Math.PI * beatPhase;

            double acrossOnce = Math.Cos(angle);
            double alongOnce = Math.Sin(angle);
            double acrossTwice = Math.Cos(2.0 * angle);
            double alongTwice = Math.Sin(2.0 * angle);

            int count = Math.Min(bandFlux.Length, _totalFlux.Length);

            for (int band = 0; band < count; band++)
            {
                double flux = bandFlux[band];

                if (flux <= 0.0)
                {
                    continue;
                }

                _acrossBeat[band] += flux * acrossOnce;
                _alongBeat[band] += flux * alongOnce;
                _acrossHalfBeat[band] += flux * acrossTwice;
                _alongHalfBeat[band] += flux * alongTwice;
                _totalFlux[band] += flux;
            }

            Recompute();
        }

        /// <summary>
        /// Forgets everything and goes back to weighting every band equally.
        /// </summary>
        public void Forget()
        {
            Array.Clear(_acrossBeat);
            Array.Clear(_alongBeat);
            Array.Clear(_acrossHalfBeat);
            Array.Clear(_alongHalfBeat);
            Array.Clear(_totalFlux);
            Array.Clear(_agreement);

            for (int band = 0; band < _weights.Length; band++)
            {
                _weights[band] = 1.0;
            }

            IsReady = false;
        }

        /// <summary>
        /// Lets the running totals decay, so old music stops counting.
        ///
        /// Everything decays by the same factor, which leaves the ratio between
        /// a band's directed total and its overall flux untouched - the
        /// agreement is a property of the music, not of how long we have been
        /// listening.
        /// </summary>
        private void Fade(double deltaSeconds)
        {
            if (MemorySeconds <= 0.0)
            {
                return;
            }

            // A long stall would otherwise wipe everything in one step, the same
            // way it would anywhere else that integrates over time.
            double safeDelta = Math.Clamp(deltaSeconds, 0.0, 1.0);
            double keep = Math.Exp(-safeDelta / MemorySeconds);

            for (int band = 0; band < _totalFlux.Length; band++)
            {
                _acrossBeat[band] *= keep;
                _alongBeat[band] *= keep;
                _acrossHalfBeat[band] *= keep;
                _alongHalfBeat[band] *= keep;
                _totalFlux[band] *= keep;
            }
        }

        /// <summary>
        /// Turns the running totals into a weight per band.
        /// </summary>
        private void Recompute()
        {
            double gathered = 0.0;
            double best = 0.0;

            for (int band = 0; band < _totalFlux.Length; band++)
            {
                gathered += _totalFlux[band];

                if (_totalFlux[band] <= 0.0)
                {
                    _agreement[band] = 0.0;
                    continue;
                }

                // How long the directed total came out, against the energy that
                // went into it. Energy always arriving at the same point in the
                // beat gives a long line; energy scattered through the beat
                // cancels itself out.
                double onceRound = Math.Sqrt(
                    (_acrossBeat[band] * _acrossBeat[band]) +
                    (_alongBeat[band] * _alongBeat[band])) / _totalFlux[band];

                double twiceRound = Math.Sqrt(
                    (_acrossHalfBeat[band] * _acrossHalfBeat[band]) +
                    (_alongHalfBeat[band] * _alongHalfBeat[band])) / _totalFlux[band];

                // Whichever is stronger. See the class notes for why ignoring the
                // faster one would punish the band carrying the beat whenever the
                // tempo had settled at half speed.
                double agreement = Math.Max(onceRound, twiceRound);

                _agreement[band] = Math.Clamp(agreement, 0.0, 1.0);

                if (_agreement[band] > best)
                {
                    best = _agreement[band];
                }
            }

            IsReady = gathered >= FluxNeededBeforeTrusting;

            if (!IsReady || best <= 0.0)
            {
                // Nothing worth saying yet, so say nothing: every band equal,
                // which is exactly how the detector behaved before any of this.
                for (int band = 0; band < _weights.Length; band++)
                {
                    _weights[band] = 1.0;
                }

                return;
            }

            // Measured against the best band rather than against a fixed scale.
            // On material where nothing agrees strongly - and there is real music
            // like that - the best band is still the one worth listening to, and
            // an absolute scale would flatten every band to its floor and throw
            // away the ordering that is the entire point.
            double total = 0.0;

            for (int band = 0; band < _weights.Length; band++)
            {
                double share = _agreement[band] / best;

                _weights[band] = SmallestShare + ((1.0 - SmallestShare) * share);
                total += _weights[band];
            }

            // Scale so the weights average 1. Without this the flux would shrink
            // whenever the weighting became uneven, and the threshold - which is
            // built from recent flux - would spend its time chasing the weighting
            // rather than the music.
            double scale = _weights.Length / total;

            for (int band = 0; band < _weights.Length; band++)
            {
                _weights[band] *= scale;
            }
        }
    }
}
