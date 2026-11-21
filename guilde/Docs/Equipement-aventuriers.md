# Équipement des aventuriers — jalon P2

## Utiliser l'armurerie

Depuis la scène GuildWorkshop, fabriquer une pièce à la Forge, puis cliquer sur **Équipement & réserves** sous le roster. On peut aussi ouvrir la fiche d'un aventurier et cliquer sur **Équiper et comparer les statistiques**.

Choisir l'aventurier puis un des sept emplacements : arme principale, main secondaire, armure, casque, gants, bottes, accessoire. Les réserves affichent les pièces correspondant à l'emplacement sélectionné et une comparaison des stats avant/après. Utiliser le bouton Équiper ou glisser la carte d'une pièce vers l'emplacement compatible. Retirer une pièce la rend immédiatement disponible dans les réserves.

L'onglet **Statistiques** montre les huit attributs effectifs et les douze stats de combat, équipement compris. **Retour à la cour** réouvre la Forge.

## Règles d'équipement

- Une instance reste unique dans GuildInventory. Seuls les identifiants dans Adventurer.equippedItems déterminent qui la porte ; aucun propriétaire supplémentaire n'est stocké sur l'objet.
- Remplacer une pièce libère automatiquement l'ancienne. Deux aventuriers ne peuvent pas porter le même objet.
- Une pièce portée ne peut pas être vendue. Le Marché et son compteur comptent uniquement les pièces disponibles.
- requiredClassId est vérifié lorsqu'il est renseigné. Sans restriction, toutes les classes peuvent porter l'objet.
- Les armes non maîtrisées restent équipables. Valeurs provisoires par arme dans EquipmentSO : dégâts physiques et magiques ×0,75, précision ×0,8, usure ×1,5.
- Les changements d'équipement sont bloqués pendant une mission et après la mort.
- Pour ce jalon, renvoi et mort restituent les pièces aux réserves. Une éventuelle perte d'équipement à la résolution d'une mission reste une décision pour P3.

## Calcul des stats

Adventurer.GetEffectiveStats retourne une copie : statistiques de classe/race, multiplications des traits et passifs de classe, puis modificateurs secondaires plats et plafonds. Aucun calcul ne modifie les données de base.

Adventurer.GetCombatStats convertit ces attributs, ajoute les stats principales et substats des pièces intactes, applique les malus d'arme non maîtrisée puis les plafonds de combat. Une stat primaire portée est convertie en contributions de combat une seule fois. L'aperçu remplace virtuellement une pièce, sans modifier le loadout.

Les attributs affichés dans la fiche incluent classe, race et traits ; la contribution des pièces apparaît dans les statistiques de combat. La Vitalité effective influence également l'accumulation de fatigue au travail.

## Usure et réparations

EquipmentManager.ApplyWear est le point d'entrée pour les futures missions et combats. Il use les pièces portées, avec un surcoût pour une arme non maîtrisée. Le travail à la Forge ne dégrade pas artificiellement les objets.

À zéro de durabilité, une pièce reste portée mais perd tous ses bonus. Sous 25 %, elle est signalée comme nécessitant une réparation.

La Forge affiche toutes les pièces endommagées, portées ou en réserve. La réparation est immédiate et exige une Forge active, un forgeron en poste sans commande en cours et assez d'or. Elle est interdite pour un objet actuellement en mission.

Prix provisoire : valeur de vente brute de la pièce × 40 % × fraction de durabilité manquante, arrondi au supérieur ; au moins 1 or pour une pièce endommagée. Une pièce intacte ne coûte rien et ne peut pas être facturée à nouveau. Ce coefficient se règle sur EquipmentManager dans la scène.

## Vérifications

- **Guildmaster > 2 - Verifier la boucle (Play)** : 32 contrôles de l'atelier.
- **Guildmaster > 3 - Verifier l'equipement (Play)** : 43 contrôles de l'équipement, des calculs et des réparations ; résultat dans Docs/AI/EquipmentValidation.json.
- **Guildmaster > 4 - Demo equipement et reparation (Play)** : prépare une partie d'essai avec un guerrier, une mage, une épée usée, une armure rare et une dague épique. Cette commande d'éditeur réinitialise la partie d'essai.

Les tests couvrent aussi le remplacement, les doublons, les restrictions de classe, le blocage en mission, les artisans occupés, l'or insuffisant, une Forge en travaux, les pièces cassées, le renvoi, la mort et la reconstruction des identifiants après sérialisation. Cette dernière vérification prépare la future sauvegarde ; elle ne constitue pas un système de sauvegarde de partie.

Dernier passage sous Unity 6000.3.11f1 : **43 + 32 vérifications réussies**, sans nouvelle erreur relevée depuis le début de P2. Vérification à la souris : remplacement d'une arme par glisser-déposer, équipement d'une armure par bouton, lecture des 20 statistiques en deux colonnes et réparation d'une épée de 20/100 à 100/100 pour 21 or. Deux passes de mise en page ont séparé les emplacements des réserves et compacté la fiche de statistiques.

## Limites du jalon

La boucle de missions reste à implémenter : l'usure a donc été vérifiée par le scénario d'éditeur, et n'est pas encore déclenchée par une activité du jeu. Les recettes actuelles produisent armes et armures ; le code et l'interface acceptent les sept emplacements pour le futur contenu.
