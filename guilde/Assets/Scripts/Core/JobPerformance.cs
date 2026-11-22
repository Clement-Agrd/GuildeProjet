using UnityEngine;

public readonly struct JobEffectBreakdown
{
    public readonly PrimaryStat Stat;
    public readonly int StatValue;
    public readonly float BaseValue;
    public readonly float StatBonus;
    public readonly float RaceBonus;
    public readonly float LevelBonus;
    public readonly float Total;

    public JobEffectBreakdown(PrimaryStat stat, int statValue, float baseValue, float statBonus,
        float raceBonus, float levelBonus, float minimum, float maximum)
    {
        Stat = stat;
        StatValue = statValue;
        BaseValue = baseValue;
        StatBonus = statBonus;
        RaceBonus = raceBonus;
        LevelBonus = levelBonus;
        Total = Mathf.Clamp(baseValue + statBonus + raceBonus + levelBonus, minimum, maximum);
    }
}

public static class JobPerformance
{
    public static JobEffectBreakdown GetEffect(Adventurer worker, JobEffectType effect, JobDefinitionSO definition = null)
    {
        var (stat, perPoint, baseValue) = effect switch
        {
            JobEffectType.ForgeDurationReduction => (PrimaryStat.Force, 0.01f, 0f),
            JobEffectType.WorkshopDurationReduction => (PrimaryStat.Intelligence, 0.01f, 0f),
            JobEffectType.ForgeQuality or JobEffectType.WorkshopQuality or JobEffectType.TrainedRecruitChance
                => (PrimaryStat.Chance, 0.005f, 0f),
            JobEffectType.SalePrice => (PrimaryStat.Charisme, 0.01f, 0f),
            JobEffectType.HealingSpeed or JobEffectType.TavernRecovery => (PrimaryStat.Sagesse, 0.02f, 0.5f),
            JobEffectType.RecruitmentSpeed => (PrimaryStat.Charisme, 0.02f, 0f),
            JobEffectType.TrainingSpeed => (PrimaryStat.Resistance, 0.02f, 0.5f),
            JobEffectType.ScoutingSpeed => (PrimaryStat.Agilite, 0.01f, 1f),
            JobEffectType.ScoutingQuality => (PrimaryStat.Chance, 0.005f, 0.25f),
            JobEffectType.WorkFatigue => (PrimaryStat.Vitalite, -0.03f, 5f),
            _ => throw new System.ArgumentOutOfRangeException(nameof(effect))
        };
        float levelBonus = effect switch
        {
            JobEffectType.ForgeDurationReduction => worker.GetJobInstance(JobType.Blacksmith)?.GetEfficiencyBonus(definition) ?? 0f,
            JobEffectType.WorkshopDurationReduction => worker.GetJobInstance(JobType.Craftsman)?.GetEfficiencyBonus(definition) ?? 0f,
            JobEffectType.ForgeQuality => worker.GetJobInstance(JobType.Blacksmith)?.GetQualityBonus(definition) ?? 0f,
            JobEffectType.WorkshopQuality => worker.GetJobInstance(JobType.Craftsman)?.GetQualityBonus(definition) ?? 0f,
            _ => 0f
        };
        float maximum = effect switch
        {
            JobEffectType.ForgeDurationReduction or JobEffectType.WorkshopDurationReduction => 0.8f,
            JobEffectType.SalePrice => 0.5f,
            _ => float.PositiveInfinity
        };
        int statValue = worker.GetEffectivePrimary(stat);
        float raceBonus = worker.adventurerRace?.GetJobEffectBonus(effect) ?? 0f;
        float minimum = effect == JobEffectType.WorkFatigue ? 0.5f : 0f;
        return new JobEffectBreakdown(stat, statValue, baseValue, statValue * perPoint, raceBonus, levelBonus, minimum, maximum);
    }

    public static (float Duration, float QualityBonus) GetCrafting(Adventurer worker, CraftRecipeSO recipe, JobDefinitionSO definition)
    {
        var (efficiency, quality) = recipe.requiredJob switch
        {
            JobType.Blacksmith => (
                GetEffect(worker, JobEffectType.ForgeDurationReduction, definition).Total,
                GetEffect(worker, JobEffectType.ForgeQuality, definition).Total),
            JobType.Craftsman => (
                GetEffect(worker, JobEffectType.WorkshopDurationReduction, definition).Total,
                GetEffect(worker, JobEffectType.WorkshopQuality, definition).Total),
            _ => (
                worker.GetJobInstance(recipe.requiredJob)?.GetEfficiencyBonus(definition) ?? 0f,
                worker.GetJobInstance(recipe.requiredJob)?.GetQualityBonus(definition) ?? 0f)
        };
        return (Mathf.Max(0.1f, recipe.GetActualDuration(efficiency)), quality);
    }

    public static float HealingBonus(Adventurer healer) => GetEffect(healer, JobEffectType.HealingSpeed).Total;
    public static float SaleBonus(Adventurer merchant) => merchant == null ? 0f : GetEffect(merchant, JobEffectType.SalePrice).Total;
    public static float TrainingBonus(Adventurer trainer) => GetEffect(trainer, JobEffectType.TrainingSpeed).Total;
    public static float TavernRecoveryBonus(Adventurer tavernkeeper) => GetEffect(tavernkeeper, JobEffectType.TavernRecovery).Total;
    public static (float SpeedBonus, float TrainedChanceBonus) Recruitment(Adventurer recruiter)
        => (GetEffect(recruiter, JobEffectType.RecruitmentSpeed).Total, GetEffect(recruiter, JobEffectType.TrainedRecruitChance).Total);

    public static (float SpeedBonus, float QualityBonus) Scouting(Adventurer scout)
        => (GetEffect(scout, JobEffectType.ScoutingSpeed).Total, GetEffect(scout, JobEffectType.ScoutingQuality).Total);
}
