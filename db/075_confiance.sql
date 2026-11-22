-- Étiquette « Confiance » : question de confiance de la Chambre des députés d'Italie (drapeau officiel richiestaFiducia). Colonne rétro-remplie d'après la note déjà écrite à l'import.
BEGIN;
ALTER TABLE scrutins ADD COLUMN IF NOT EXISTS confiance boolean NOT NULL DEFAULT false;
UPDATE scrutins SET confiance = true WHERE chambre = 'ITCAMERA' AND note LIKE '%Question of confidence%';
INSERT INTO traductions (langue, cle, texte, source_fr) VALUES
('fr', $t$confiance-etiquette$t$, $t$Confiance$t$, $t$Confiance$t$),
('en', $t$confiance-etiquette$t$, $t$Confidence$t$, $t$Confiance$t$),
('de', $t$confiance-etiquette$t$, $t$Vertrauen$t$, $t$Confiance$t$),
('it', $t$confiance-etiquette$t$, $t$Fiducia$t$, $t$Confiance$t$),
('fr', $t$confiance-infobulle$t$, $t$Question de confiance : le gouvernement a engagé sa responsabilité sur ce texte. Le vote porte sur la confiance accordée au gouvernement ; les amendements tombent si elle est accordée.$t$, $t$Question de confiance : le gouvernement a engagé sa responsabilité sur ce texte. Le vote porte sur la confiance accordée au gouvernement ; les amendements tombent si elle est accordée.$t$),
('en', $t$confiance-infobulle$t$, $t$Confidence vote: the government staked its survival on this text. The vote is about confidence in the government; amendments lapse if it is granted.$t$, $t$Question de confiance : le gouvernement a engagé sa responsabilité sur ce texte. Le vote porte sur la confiance accordée au gouvernement ; les amendements tombent si elle est accordée.$t$),
('de', $t$confiance-infobulle$t$, $t$Vertrauensfrage: Die Regierung hat ihr Schicksal an diesen Text geknüpft. Abgestimmt wird über das Vertrauen in die Regierung; Änderungsanträge entfallen, wenn es ausgesprochen wird.$t$, $t$Question de confiance : le gouvernement a engagé sa responsabilité sur ce texte. Le vote porte sur la confiance accordée au gouvernement ; les amendements tombent si elle est accordée.$t$),
('it', $t$confiance-infobulle$t$, $t$Questione di fiducia: il governo ha posto la fiducia su questo testo. Si vota sulla fiducia al governo; gli emendamenti decadono se è accordata.$t$, $t$Question de confiance : le gouvernement a engagé sa responsabilité sur ce texte. Le vote porte sur la confiance accordée au gouvernement ; les amendements tombent si elle est accordée.$t$)
ON CONFLICT DO NOTHING;
COMMIT;
