﻿// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko.Judgements;
using osu.Game.Rulesets.Taiko.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Taiko.Scoring
{
    internal class TaikoScoreProcessor : ScoreProcessor<TaikoHitObject>
    {
        /// <summary>
        /// The HP awarded by a <see cref="HitResult.Great"/> hit.
        /// </summary>
        private const double hp_hit_great = 0.03;

        /// <summary>
        /// The HP awarded for a <see cref="HitResult.Good"/> hit.
        /// </summary>
        private const double hp_hit_good = 0.011;

        /// <summary>
        /// The minimum HP deducted for a <see cref="HitResult.Miss"/>.
        /// This occurs when HP Drain = 0.
        /// </summary>
        private const double hp_miss_min = -0.0018;

        /// <summary>
        /// The median HP deducted for a <see cref="HitResult.Miss"/>.
        /// This occurs when HP Drain = 5.
        /// </summary>
        private const double hp_miss_mid = -0.0075;

        /// <summary>
        /// The maximum HP deducted for a <see cref="HitResult.Miss"/>.
        /// This occurs when HP Drain = 10.
        /// </summary>
        private const double hp_miss_max = -0.12;

        /// <summary>
        /// The HP awarded for a <see cref="DrumRollTick"/> hit.
        /// <para>
        /// <see cref="DrumRollTick"/> hits award less HP as they're more spammable, although in hindsight
        /// this probably awards too little HP and is kept at this value for now for compatibility.
        /// </para>
        /// </summary>
        private const double hp_hit_tick = 0.00000003;

        /// <summary>
        /// Taiko fails at the end of the map if the player has not half-filled their HP bar.
        /// </summary>
        protected override bool DefaultFailCondition => JudgedHits == MaxHits && Health.Value <= 0.5;

        private double hpIncreaseTick;
        private double hpIncreaseGreat;
        private double hpIncreaseGood;
        private double hpIncreaseMiss;

        public TaikoScoreProcessor(RulesetContainer<TaikoHitObject> rulesetContainer)
            : base(rulesetContainer)
        {
        }

        protected override void ApplyBeatmap(Beatmap<TaikoHitObject> beatmap)
        {
            base.ApplyBeatmap(beatmap);

            double hpMultiplierNormal = 1 / (hp_hit_great * beatmap.HitObjects.FindAll(o => o is Hit).Count * BeatmapDifficulty.DifficultyRange(beatmap.BeatmapInfo.BaseDifficulty.DrainRate, 0.5, 0.75, 0.98));

            hpIncreaseTick = hp_hit_tick;
            hpIncreaseGreat = hpMultiplierNormal * hp_hit_great;
            hpIncreaseGood = hpMultiplierNormal * hp_hit_good;
            hpIncreaseMiss = BeatmapDifficulty.DifficultyRange(beatmap.BeatmapInfo.BaseDifficulty.DrainRate, hp_miss_min, hp_miss_mid, hp_miss_max);
        }

        protected override void ApplyResult(JudgementResult result)
        {
            base.ApplyResult(result);

            bool isTick = result.Judgement is TaikoDrumRollTickJudgement;

            // Apply HP changes
            switch (result.Type)
            {
                case HitResult.Miss:
                    // Missing ticks shouldn't drop HP
                    if (!isTick)
                        Health.Value += hpIncreaseMiss;
                    break;
                case HitResult.Good:
                    Health.Value += hpIncreaseGood;
                    break;
                case HitResult.Great:
                    if (isTick)
                        Health.Value += hpIncreaseTick;
                    else
                        Health.Value += hpIncreaseGreat;
                    break;
            }
        }

        protected override void Reset(bool storeResults)
        {
            base.Reset(storeResults);

            Health.Value = 0;
        }
    }
}
