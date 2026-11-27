-- Pied de page : titres des colonnes de liens.

INSERT INTO traductions (langue, cle, texte, source_fr) VALUES
($t$fr$t$, $t$pied-titre-donnees$t$, $t$Les données$t$, $t$Les données$t$),
($t$en$t$, $t$pied-titre-donnees$t$, $t$The data$t$, $t$Les données$t$),
($t$de$t$, $t$pied-titre-donnees$t$, $t$Die Daten$t$, $t$Les données$t$),
($t$it$t$, $t$pied-titre-donnees$t$, $t$I dati$t$, $t$Les données$t$),
($t$fr$t$, $t$pied-titre-projet$t$, $t$Le projet$t$, $t$Le projet$t$),
($t$en$t$, $t$pied-titre-projet$t$, $t$The project$t$, $t$Le projet$t$),
($t$de$t$, $t$pied-titre-projet$t$, $t$Das Projekt$t$, $t$Le projet$t$),
($t$it$t$, $t$pied-titre-projet$t$, $t$Il progetto$t$, $t$Le projet$t$),
($t$fr$t$, $t$pied-titre-infos$t$, $t$Informations$t$, $t$Informations$t$),
($t$en$t$, $t$pied-titre-infos$t$, $t$Information$t$, $t$Informations$t$),
($t$de$t$, $t$pied-titre-infos$t$, $t$Informationen$t$, $t$Informations$t$),
($t$it$t$, $t$pied-titre-infos$t$, $t$Informazioni$t$, $t$Informations$t$)
ON CONFLICT DO NOTHING;
