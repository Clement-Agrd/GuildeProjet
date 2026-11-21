using System;
using UnityEngine;

[Serializable]
public class CombatStats
{
    [Header("Points de vie & défense")]
    public int   pointsDeVie;
    public int   defensePhysique;
    public int   defenseMagique;

    [Header("Attaque")]
    public int   attaquePhysique;
    public int   attaqueMagique;

    [Header("Critique")]
    public float chanceCrit;        // %
    public float degatsCrit;        // % total (150 = 150% des dégâts)
    public float resistanceCrit;    // % de réduction de la chance crit adverse

    [Header("Mobilité")]
    public float esquive;           // %
    public float precision;         // %

    [Header("Soins")]
    public int   puissanceSoin;
    public float tauxRecuperation;  // %

    // ─── Calcul de base depuis les stats primaires ────────────────────────────

    public static CombatStats FromPrimary(AdventurerStats s) => new CombatStats
    {
        pointsDeVie      = 50   + s.vitalite * 10 + s.resistance * 2,
        defensePhysique  = 5    + s.vitalite *  2 + s.resistance * 5,
        defenseMagique   = 5    + s.resistance * 4 + s.charisme  * 3,
        attaquePhysique  = 10   + s.force * 8,
        attaqueMagique   = 10   + s.intelligence * 6 + s.charisme * 2,
        chanceCrit       = 5f   + s.chance  * 0.5f,
        degatsCrit       = 150f + s.chance  * 0.3f + s.agilite * 0.1f,
        resistanceCrit   = 0f   + s.sagesse * 0.8f,
        esquive          = 0f   + s.agilite * 0.6f,
        precision        = 90f  + s.chance  * 0.4f + s.agilite * 0.2f,
        puissanceSoin    = 0    + s.sagesse * 8,
        tauxRecuperation = 0f   + s.sagesse * 0.5f
    };

    // ─── Contribution d'une stat de combat (substats équipement) ─────────────

    public void AddCombatStat(CombatStat stat, float value)
    {
        switch (stat)
        {
            case CombatStat.PointsDeVie:      pointsDeVie      += (int)value; break;
            case CombatStat.DefensePhysique:  defensePhysique  += (int)value; break;
            case CombatStat.DefenseMagique:   defenseMagique   += (int)value; break;
            case CombatStat.AttaquePhysique:  attaquePhysique  += (int)value; break;
            case CombatStat.AttaqueMagique:   attaqueMagique   += (int)value; break;
            case CombatStat.ChanceCrit:       chanceCrit       += value;      break;
            case CombatStat.DegatsCrit:       degatsCrit       += value;      break;
            case CombatStat.ResistanceCrit:   resistanceCrit   += value;      break;
            case CombatStat.Esquive:          esquive          += value;      break;
            case CombatStat.Precision:        precision        += value;      break;
            case CombatStat.PuissanceSoin:    puissanceSoin    += (int)value; break;
            case CombatStat.TauxRecuperation: tauxRecuperation += value;      break;
        }
    }

    // ─── Contribution d'une stat primaire (stat principale d'équipement) ─────
    // Même formule de conversion que FromPrimary — s'additionne sur la base

    public void AddPrimary(PrimaryStat stat, int value)
    {
        switch (stat)
        {
            case PrimaryStat.Vitalite:
                pointsDeVie     += value * 10;
                defensePhysique += value * 2;
                break;
            case PrimaryStat.Resistance:
                pointsDeVie     += value * 2;
                defensePhysique += value * 5;
                defenseMagique  += value * 4;
                break;
            case PrimaryStat.Force:
                attaquePhysique += value * 8;
                break;
            case PrimaryStat.Intelligence:
                attaqueMagique += value * 6;
                break;
            case PrimaryStat.Charisme:
                attaqueMagique += value * 2;
                defenseMagique += value * 3;
                break;
            case PrimaryStat.Sagesse:
                puissanceSoin    += value * 8;
                resistanceCrit   += value * 0.8f;
                tauxRecuperation += value * 0.5f;
                break;
            case PrimaryStat.Agilite:
                esquive    += value * 0.6f;
                precision  += value * 0.2f;
                degatsCrit += value * 0.1f;
                break;
            case PrimaryStat.Chance:
                chanceCrit  += value * 0.5f;
                precision   += value * 0.4f;
                degatsCrit  += value * 0.3f;
                break;
        }
    }

    // ─── Clamp anti-absurdité ─────────────────────────────────────────────────

    public void Clamp()
    {
        pointsDeVie      = Mathf.Max(1, pointsDeVie);
        defensePhysique  = Mathf.Max(0, defensePhysique);
        defenseMagique   = Mathf.Max(0, defenseMagique);
        attaquePhysique  = Mathf.Max(0, attaquePhysique);
        attaqueMagique   = Mathf.Max(0, attaqueMagique);
        chanceCrit       = Mathf.Clamp(chanceCrit,       0f,   75f);
        degatsCrit       = Mathf.Clamp(degatsCrit,       100f, 500f);
        resistanceCrit   = Mathf.Clamp(resistanceCrit,   0f,   75f);
        esquive          = Mathf.Clamp(esquive,          0f,   60f);
        precision        = Mathf.Clamp(precision,        0f,   100f);
        puissanceSoin    = Mathf.Max(0, puissanceSoin);
        tauxRecuperation = Mathf.Clamp(tauxRecuperation, 0f,   50f);
    }
}
