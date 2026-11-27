-- Projets de loi britanniques : sessions où figure chaque projet (champ « includedSessionIds » de la source). Un projet reporté figure dans plusieurs.
-- Sans elles, un projet d'une session close, jamais devenu loi, restait affiché « en cours ». Remplies par la tâche « bills_uk » (NULL = pas encore lu).

BEGIN;

ALTER TABLE bills ADD COLUMN IF NOT EXISTS sessions integer[];

INSERT INTO traductions (langue, cle, texte, source_fr) VALUES
($t$fr$t$, $t$bill-session-close$t$, $t$Session close$t$, $t$Session close$t$),
($t$en$t$, $t$bill-session-close$t$, $t$Session closed$t$, $t$Session close$t$),
($t$de$t$, $t$bill-session-close$t$, $t$Sitzungsperiode beendet$t$, $t$Session close$t$),
($t$it$t$, $t$bill-session-close$t$, $t$Sessione chiusa$t$, $t$Session close$t$),
($t$fr$t$, $t$bill-session-close-aide$t$, $t$Calculé : la dernière session parlementaire où figure ce projet est terminée, sans sanction royale, retrait ni rejet publiés.$t$, $t$Calculé : la dernière session parlementaire où figure ce projet est terminée, sans sanction royale, retrait ni rejet publiés.$t$),
($t$en$t$, $t$bill-session-close-aide$t$, $t$Computed: the last parliamentary session listing this bill has ended, with no Royal Assent, withdrawal or defeat published.$t$, $t$Calculé : la dernière session parlementaire où figure ce projet est terminée, sans sanction royale, retrait ni rejet publiés.$t$),
($t$de$t$, $t$bill-session-close-aide$t$, $t$Berechnet: Die letzte Sitzungsperiode, in der dieser Entwurf geführt wird, ist beendet, ohne veröffentlichte königliche Zustimmung, Rücknahme oder Ablehnung.$t$, $t$Calculé : la dernière session parlementaire où figure ce projet est terminée, sans sanction royale, retrait ni rejet publiés.$t$),
($t$it$t$, $t$bill-session-close-aide$t$, $t$Calcolato: l'ultima sessione parlamentare in cui figura il disegno è terminata, senza approvazione reale, ritiro o rigetto pubblicati.$t$, $t$Calculé : la dernière session parlementaire où figure ce projet est terminée, sans sanction royale, retrait ni rejet publiés.$t$),
($t$fr$t$, $t$bills-sessions-closes$t$, $t$Sessions closes sans loi$t$, $t$Sessions closes sans loi$t$),
($t$en$t$, $t$bills-sessions-closes$t$, $t$Session closed without Royal Assent$t$, $t$Sessions closes sans loi$t$),
($t$de$t$, $t$bills-sessions-closes$t$, $t$Beendet ohne königliche Zustimmung$t$, $t$Sessions closes sans loi$t$),
($t$it$t$, $t$bills-sessions-closes$t$, $t$Sessione chiusa senza approvazione reale$t$, $t$Sessions closes sans loi$t$)
ON CONFLICT DO NOTHING;

COMMIT;
