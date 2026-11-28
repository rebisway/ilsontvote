-- Page « Données en anomalie » : nouveau texte d'introduction. Remplace seulement le texte d'origine (migration 089) : une correction de l'administration est conservée.

BEGIN;

UPDATE traductions SET texte = $t$Les anomalies détectées dans les données envoyées par les fournisseurs de données.$t$ WHERE cle = 'anom-chapo' AND langue = 'fr' AND texte = $t$Ce que le site sait ne pas savoir. Ces constats sont calculés à partir de notre base : ils peuvent venir de la source comme de notre import.$t$;
UPDATE traductions SET texte = $t$Anomalies detected in the data sent by data providers.$t$ WHERE cle = 'anom-chapo' AND langue = 'en' AND texte = $t$What this site knows it does not know. These findings are computed from our database: they may come from the source or from our own import.$t$;
UPDATE traductions SET texte = $t$In den Daten der Datenlieferanten festgestellte Auffälligkeiten.$t$ WHERE cle = 'anom-chapo' AND langue = 'de' AND texte = $t$Was diese Website nachweislich nicht weiß. Diese Befunde werden aus unserer Datenbank berechnet: Sie können von der Quelle oder von unserem Import stammen.$t$;
UPDATE traductions SET texte = $t$Le anomalie rilevate nei dati inviati dai fornitori di dati.$t$ WHERE cle = 'anom-chapo' AND langue = 'it' AND texte = $t$Ciò che il sito sa di non sapere. Queste constatazioni sono calcolate dalla nostra banca dati: possono dipendere dalla fonte o dalla nostra importazione.$t$;
-- La ligne française a changé : les autres langues s'y rattachent.
UPDATE traductions SET source_fr = (SELECT texte FROM traductions WHERE cle = 'anom-chapo' AND langue = 'fr') WHERE cle = 'anom-chapo';

COMMIT;
