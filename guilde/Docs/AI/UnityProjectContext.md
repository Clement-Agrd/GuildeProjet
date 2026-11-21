# GuildeMaster — contexte Unity

<!-- unity-onboarding:generated:start -->
Analyse initiale : 21 novembre 2026 ; base Git `98eeeb0`, nombreux scripts modifiés par l'utilisateur avant l'intervention. Ces modifications sont conservées. Copie de travail initiale dans `Temp/restore/workshop-20261121`.

## Environnement confirmé

- Racine : `guilde/` ; Unity **6000.3.11f1**, PC.
- URP **17.3.0**, renderer 2D configuré par les niveaux de qualité ; Input System **1.19.0** (`activeInputHandler: 1`).
- uGUI 2.0.0, UI Toolkit intégré, Test Framework 1.6.0. Aucun framework réseau utilisé par le code de jeu.
- Aucun MCP ni package Pipeline dans le manifeste. CLI Unity 1.0.0 installé ; découverte de l'éditeur inaccessible depuis son contexte. Fenêtre Unity confirmée par inventaire des applications.

## Architecture et conventions

Définitions ScriptableObject dans `Assets/Scripts/Data`, état sérialisable dans `Runtime`, calculs et enums dans `Core`, orchestration dans `Managers`. Pas de namespace ni d'asmdef de jeu. Managers MonoBehaviour avec `Instance` et événements C#. Les définitions restent immuables pendant la partie ; les références runtime non sérialisées sont reconstruites par identifiant d'asset. Champs existants et GUID préservés.

21 scripts initiaux lus, dont les quatre managers et toutes les instances de jeu. Initialement aucun appel aux `Tick`, aucun contenu dans `Assets/Data`, aucune UI. Seule scène de build : `Assets/Scenes/SampleScene.unity`. Aucun test de jeu ou CI trouvé.

## Contrat du prochain jalon

Le document technique `Guildmaster — Document technique(1).docx` a été lu intégralement (16 pages). Son jalon C6/P1 est la cible : recrutement, affectation, fabrication, vente, contrôle du temps et interface de gestion. Les missions, expéditions, sauvegarde et équipement porté attendent les jalons suivants.

Conserver provisoirement le salaire à la prise de poste, les commissions prélevées et les formules de progression existantes. Ne pas installer de package, migrer Unity ni remplacer l'architecture pour ce jalon. Les scènes et assets seront créés par les API de l'éditeur, pas par écriture de YAML.

## Sources principales

`ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `ProjectSettings/{EditorBuildSettings,GraphicsSettings,QualitySettings,ProjectSettings}.asset`, tous les fichiers de `Assets/Scripts`, README racine et document technique complet.
<!-- unity-onboarding:generated:end -->

## Jalon atelier implémenté le 21 novembre 2026

- Entrée jouable : Assets/Scenes/GuildWorkshop.unity, première scène de build ; SampleScene conservée.
- Initialisation : GameManager en Start, après les Awake des quatre managers ; TimeManager appelle économie, bâtiments, aventuriers par pas de 0,1 s, avec temps non mis à l'échelle et multiplicateur de vitesse propre au jeu.
- UI Toolkit confirmé par le choix utilisateur. UXML, USS, thème et PanelSettings sous Assets/UI/Workshop ; contrôleurs sous Assets/Scripts/UI. UIDocument compatible Unity 6000.3 ; pas de migration vers les composants des versions ultérieures.
- 24 définitions sous Assets/Data/Workshop, créées via AssetDatabase. Les assets et la scène existants sont préservés par le menu de préparation.
- Correctifs : contribution Résistance aux PV, métiers absents, emplacements libérés au repos/blessure/mort, une commande par artisan, pause/reprise et annulation, traçabilité du métier, salaire prévisualisé pour le poste demandé, achat refusé au-delà de la pile.
- Deux outils Editor : GuildWorkshopSetup et GuildWorkshopValidation. Le second exerce les vrais systèmes en Play et écrit Docs/AI/WorkshopValidation.json.
- Validation finale : 32 vérifications réussies en Play après le dernier import, sans nouvelle erreur de compilation ou d'exécution relevée depuis la correction des références. Recrutement, glisser-déposer, fabrication d'épée et vente vérifiés aussi à la souris. Deux passes visuelles : textes agrandis et titre corrigé, puis barres de défilement harmonisées ; écran final relu dans la vue Game agrandie.
- État laissé dans Unity : GuildWorkshop ouverte en Play, simulation en pause, nouvelle partie intacte.
- Aucun package ajouté. Aucune migration Unity, aucun commit ni push ; modifications utilisateur initiales conservées.
- Guide de lancement et limites : Docs/Atelier-jouable.md. Missions, équipement porté et sauvegarde restent à faire.

## Jalon P2 — équipement des aventuriers

- EquipmentManager ajouté dans la scène par le menu de préparation, références sérialisées vers inventaire, aventuriers, bâtiments et économie. Aucune dépendance ajoutée à GuildInventory.
- Adventurer.equippedItems contient uniquement des couples emplacement/identifiant. Les instances restent uniques dans l'inventaire ; GetOwner dérive l'appartenance du roster, sans cache ni seconde source de vérité. Les réserves et les ventes excluent les objets portés.
- Adventurer.GetEffectiveStats compose traits et passifs sur une copie ; GetCombatStats applique les pièces intactes, la maîtrise des armes et les plafonds. Le résolveur des instances est injecté lors de RegisterAdventurer, y compris après chargement.
- EquipmentSO reçoit des paramètres de maîtrise provisoires : dégâts ×0,75, précision ×0,8, usure ×1,5. La réparation coûte 40 % de la valeur brute au prorata de l'usure, avec Forge active et forgeron libre. Les paramètres sont modifiables dans les assets ou sur le manager.
- UI : armurerie occupant l'espace central, roster conservé, emplacements et réserves en colonnes séparées, glisser-déposer, aperçu, retrait, statistiques complètes et réparations depuis la Forge.
- GuildEquipmentValidation ajoute 43 vérifications Play ; le scénario d'atelier de 32 vérifications continue de passer. Menu séparé de démonstration pour une épée usée et des pièces rares/épiques.
- Usure disponible via EquipmentManager.ApplyWear pour P3 ; elle n'est pas déclenchée artificiellement par le temps d'atelier. Mort et renvoi rendent provisoirement les objets aux réserves.
- Guide détaillé : Docs/Equipement-aventuriers.md. Prochain jalon fonctionnel : missions (P3). Sauvegarde de partie toujours absente.
- Point de retour avant P2 : Temp/restore/equipment-20261121. Modifications utilisateur conservées ; aucun package, commit ou push ajouté.
- Contrôle final P2 : 75 vérifications réussies après l'import final, aucune nouvelle erreur de compilation/exécution. Dépôt d'une arme, bouton d'équipement d'armure, stats en deux colonnes et réparation depuis la Forge vérifiés à la souris. Métadonnées présentes et aucun GUID dupliqué.
- État laissé dans Unity après P2 : Play en pause, armurerie ouverte, démo avec Aldric et Liora, épée réparée, armure rare et dague épique en réserve ; Aldric affecté à la Forge. Cette partie d'essai reste non sauvegardée.
