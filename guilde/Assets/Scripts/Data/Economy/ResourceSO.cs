using UnityEngine;

[CreateAssetMenu(fileName = "Resource_New", menuName = "Guildmaster/Economy/Resource")]
public class ResourceSO : ScriptableObject
{
    [Header("Identité")]
    public string resourceName;
    [TextArea(1, 3)] public string description;
    public ResourceType resourceType;
    public Sprite icon;

    [Header("Économie")]
    public int baseMarketValue = 10;

    [Range(0f, 1f)]
    [Tooltip("Amplitude de fluctuation du prix marché (0.2 = ±20%)")]
    public float priceVolatility = 0.1f;

    [Header("Inventaire")]
    public int maxStackSize = 999;
}