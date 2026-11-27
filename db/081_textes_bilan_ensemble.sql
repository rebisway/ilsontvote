-- Fiche d'un élu : second bilan, limité aux votes sur l'ensemble d'un texte.

INSERT INTO traductions (langue, cle, texte, source_fr) VALUES
($t$fr$t$, $t$elu-ensemble-titre$t$, $t$Participation aux votes sur l'ensemble d'un texte$t$, $t$Participation aux votes sur l'ensemble d'un texte$t$),
($t$en$t$, $t$elu-ensemble-titre$t$, $t$Participation in final votes on a whole text$t$, $t$Participation aux votes sur l'ensemble d'un texte$t$),
($t$de$t$, $t$elu-ensemble-titre$t$, $t$Teilnahme an den Gesamtabstimmungen über eine Vorlage$t$, $t$Participation aux votes sur l'ensemble d'un texte$t$),
($t$it$t$, $t$elu-ensemble-titre$t$, $t$Partecipazione ai voti finali su un testo$t$, $t$Participation aux votes sur l'ensemble d'un texte$t$),
($t$fr$t$, $t$elu-ensemble-chiffre$t$, $t$des {0:N0} votes sur l'ensemble d'un texte tenus pendant son mandat$t$, $t$des {0:N0} votes sur l'ensemble d'un texte tenus pendant son mandat$t$),
($t$en$t$, $t$elu-ensemble-chiffre$t$, $t$of the {0:N0} final votes held during their term$t$, $t$des {0:N0} votes sur l'ensemble d'un texte tenus pendant son mandat$t$),
($t$de$t$, $t$elu-ensemble-chiffre$t$, $t$der {0:N0} Gesamtabstimmungen während der Amtszeit$t$, $t$des {0:N0} votes sur l'ensemble d'un texte tenus pendant son mandat$t$),
($t$it$t$, $t$elu-ensemble-chiffre$t$, $t$dei {0:N0} voti finali tenuti durante il mandato$t$, $t$des {0:N0} votes sur l'ensemble d'un texte tenus pendant son mandat$t$),
($t$fr$t$, $t$elu-ensemble-note$t$, $t$Calculé : les votes sur l'ensemble sont repérés d'après leur titre. Ils comprennent les résolutions et chaque lecture d'un texte.$t$, $t$Calculé : les votes sur l'ensemble sont repérés d'après leur titre. Ils comprennent les résolutions et chaque lecture d'un texte.$t$),
($t$en$t$, $t$elu-ensemble-note$t$, $t$Computed: final votes are identified from their title. They include resolutions and every reading of a text.$t$, $t$Calculé : les votes sur l'ensemble sont repérés d'après leur titre. Ils comprennent les résolutions et chaque lecture d'un texte.$t$),
($t$de$t$, $t$elu-ensemble-note$t$, $t$Berechnet: Gesamtabstimmungen werden anhand ihres Titels erkannt. Sie umfassen Entschließungen und jede Lesung einer Vorlage.$t$, $t$Calculé : les votes sur l'ensemble sont repérés d'après leur titre. Ils comprennent les résolutions et chaque lecture d'un texte.$t$),
($t$it$t$, $t$elu-ensemble-note$t$, $t$Calcolato: i voti finali sono individuati dal titolo. Comprendono le risoluzioni e ogni lettura di un testo.$t$, $t$Calculé : les votes sur l'ensemble sont repérés d'après leur titre. Ils comprennent les résolutions et chaque lecture d'un texte.$t$)
ON CONFLICT DO NOTHING;
