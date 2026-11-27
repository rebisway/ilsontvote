-- Projets de loi britanniques : le statut « session close » est calculé, le chapeau le dit. Seule la phrase d'origine est remplacée (une correction manuelle est conservée).

BEGIN;

UPDATE traductions SET texte = replace(texte, $t$Le statut est celui publié par la source.$t$, $t$Le statut est celui publié par la source, sauf « Sessions closes sans loi », calculé par ce site.$t$) WHERE cle = 'projets-de-loi-des-58e-et' AND langue = 'fr';
UPDATE traductions SET texte = replace(texte, $t$Status is as published by the source.$t$, $t$Status is as published by the source, except “Session closed without Royal Assent”, computed by this site.$t$) WHERE cle = 'projets-de-loi-des-58e-et' AND langue = 'en';
UPDATE traductions SET texte = replace(texte, $t$Der Status ist der von der Quelle veröffentlichte.$t$, $t$Der Status ist der von der Quelle veröffentlichte, außer „Beendet ohne königliche Zustimmung“, das diese Website berechnet.$t$) WHERE cle = 'projets-de-loi-des-58e-et' AND langue = 'de';
UPDATE traductions SET texte = replace(texte, $t$Lo stato è quello pubblicato dalla fonte.$t$, $t$Lo stato è quello pubblicato dalla fonte, salvo «Sessione chiusa senza approvazione reale», calcolato da questo sito.$t$) WHERE cle = 'projets-de-loi-des-58e-et' AND langue = 'it';
-- La ligne française change : les autres langues restent rattachées à leur source.
UPDATE traductions SET source_fr = replace(source_fr, $t$Le statut est celui publié par la source.$t$, $t$Le statut est celui publié par la source, sauf « Sessions closes sans loi », calculé par ce site.$t$) WHERE cle = 'projets-de-loi-des-58e-et';

COMMIT;
