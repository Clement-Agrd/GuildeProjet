# Première boucle jouable — 21 novembre 2026

Cible : jalon C6/P1 du document technique intégral (16 pages), avec interface P7.

## Lancer

Ouvrir **Assets/Scenes/GuildWorkshop.unity**, puis Play. Le menu **Guildmaster > 1 - Creer ou ouvrir l'atelier** crée le contenu s'il manque, ouvre la scène et répare uniquement les listes de références manquantes. Il conserve les assets et les valeurs déjà personnalisés.

1. Recruter un candidat dans le panneau de droite.
2. Glisser sa carte depuis le roster sur le bouton **LA FORGE**, ou sélectionner l'aventurier et utiliser le bouton d'affectation.
3. Cliquer sur la Forge puis lancer une épée, une dague ou une armure.
4. Suivre la commande. Une fois terminée, ouvrir le Marché et vendre la pièce.
5. Acheter des composants pour continuer, recruter un autre artisan et agrandir la Forge.

Le HUD propose pause et vitesses ×1, ×2, ×3. Les achats et affectations restent accessibles en pause. La simulation seule est arrêtée ; l'interface continue de répondre.

## Contenu créé

24 ScriptableObjects sous Assets/Data/Workshop : 2 classes, 2 races, 3 traits, les 6 métiers, Forge et Marché avec 4 niveaux, 3 ressources, 3 équipements et 3 recettes. Départ : 500 or, 18 fer, 20 bois, 12 cuir, 4 candidats, aucun aventurier imposé.

Interface UI Toolkit : UXML/USS modifiables sous Assets/UI/Workshop, contrôleurs sous Assets/Scripts/UI. Illustration vectorielle isométrique provisoire ; aucun asset graphique externe ni package ajouté.

## Règles retenues

- Scène de démarrage unique ; pas de DontDestroyOnLoad pour ces managers.
- Tous les ticks passent par TimeManager, en pas de 0,1 s. Ordre : économie, bâtiments, aventuriers. Pause et accélération ne changent pas Time.timeScale.
- Une commande à la fois par artisan, y compris si sa commande est suspendue dans un autre bâtiment.
- Le repos forcé, la blessure et la mort libèrent l'emplacement. La commande inachevée conserve sa progression et attend le retour du même artisan dans le même bâtiment.
- Annuler une commande rend les composants dans la limite des piles. Un renvoi ou un décès annule ses commandes.
- Production interdite sans artisan, avec un autre métier, pendant les travaux, sous le niveau requis ou sans composants. Les bâtiments sans métier défini refusent l'affectation.
- Salaire à la prise de poste et commission à la vente conservés comme règles provisoires du code initial.
- Améliorer la Forge suspend la production ; les artisans en poste continuent à accumuler de la fatigue.
- Le marché refuse les achats dépassant la capacité de stockage avant de dépenser l'or.
- Nouvelle partie remet à zéro le roster, le stock, les commandes, les bâtiments, les prix et le temps. Une confirmation est affichée dans l'interface.

## Vérifier

En mode Play dans GuildWorkshop : **Guildmaster > 2 - Verifier la boucle (Play)**. Le scénario exerce les vrais managers et les vrais assets : recrutement, salaire, restrictions de production, pause/vitesses, épuisement, pause/reprise de commande, XP, provenance, vente et commission, annulation, changement de métier, amélioration et nouvelle partie. Il réinitialise sa partie d'essai et laisse le jeu en pause.

Résultats : Docs/AI/WorkshopValidation.json. Ce scénario appartient uniquement à l'éditeur et n'est pas embarqué dans le jeu.

Résultat du 21 novembre : **32 vérifications réussies sous Unity 6000.3.11f1**, après l'import final. La boucle a aussi été parcourue à la souris : recrutement, dépôt dans la Forge, fabrication d'une épée normale, vente pour 58 or nets. Aucun build de distribution ni sauvegarde persistante validé à ce stade.

## Suite du document technique

P2 équipement porté et stats effectives est implémenté : voir Docs/Equipement-aventuriers.md. Prochains jalons : P3 missions ; P4 effets des bâtiments et métiers de service ; P5 réputation et événements ; P6 sauvegarde ; P8 grandes expéditions. Rien n'est enregistré entre deux sessions de Play à ce stade. Les métiers de service sont définis mais leurs effets spécifiques attendent P4.

Points de design encore ouverts : croissance des stats générales, cadence définitive des salaires, bijoutier, vitesse de combat, portefeuille des aventuriers et nom final du jeu.
