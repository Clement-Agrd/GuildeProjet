using System;
using UnityEngine;

[Serializable]
public class AffinityData
{
    public string targetAdventurerId;
    [Range(0f, 100f)] public float value = 50f;
    public AffinityType affinityType = AffinityType.Stranger;

    private const float BEST_FRIEND_THRESHOLD = 85f;
    private const float FRIEND_THRESHOLD      = 65f;
    private const float STRANGER_THRESHOLD    = 40f;
    private const float RIVAL_THRESHOLD       = 20f;

    public AffinityData() { }

    public AffinityData(string targetId, float startValue = 50f)
    {
        targetAdventurerId = targetId;
        value = Mathf.Clamp(startValue, 0f, 100f);
        RefreshType();
    }

    public void ChangeValue(float delta)
    {
        value = Mathf.Clamp(value + delta, 0f, 100f);
        RefreshType();
    }

    private void RefreshType()
    {
        affinityType = value switch
        {
            >= BEST_FRIEND_THRESHOLD => AffinityType.BestFriend,
            >= FRIEND_THRESHOLD      => AffinityType.Friend,
            >= STRANGER_THRESHOLD    => AffinityType.Stranger,
            >= RIVAL_THRESHOLD       => AffinityType.Rival,
            _                        => AffinityType.Enemy
        };
    }

    // Bonus/malus à appliquer lors du calcul de réussite de mission
    public float GetMissionBonus() => affinityType switch
    {
        AffinityType.BestFriend => 0.20f,
        AffinityType.Friend     => 0.10f,
        AffinityType.Lover      => 0.15f,
        AffinityType.Mentor     => 0.10f,
        AffinityType.Stranger   =>  0f,
        AffinityType.Rival      => -0.10f,
        AffinityType.Enemy      => -0.25f,
        _                       =>  0f
    };
}   