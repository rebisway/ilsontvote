-- Serveur MCP : la page publique et les conditions disent que la limite de débit peut être relevée pour des tests et des audits.
-- La phrase est ajoutée à la suite du texte existant (corrections manuelles conservées), une seule fois.

BEGIN;

UPDATE traductions SET texte = texte || $t$ Elle peut être relevée pour des tests et des audits, sur demande.$t$ WHERE cle = 'mcp-limite-debit' AND langue = 'fr' AND position($t$ Elle peut être relevée pour des tests et des audits, sur demande.$t$ in texte) = 0;
UPDATE traductions SET texte = texte || $t$ It can be raised for tests and audits, on request.$t$ WHERE cle = 'mcp-limite-debit' AND langue = 'en' AND position($t$ It can be raised for tests and audits, on request.$t$ in texte) = 0;
UPDATE traductions SET texte = texte || $t$ Für Tests und Prüfungen kann sie auf Anfrage angehoben werden.$t$ WHERE cle = 'mcp-limite-debit' AND langue = 'de' AND position($t$ Für Tests und Prüfungen kann sie auf Anfrage angehoben werden.$t$ in texte) = 0;
UPDATE traductions SET texte = texte || $t$ Può essere aumentato per test e verifiche, su richiesta.$t$ WHERE cle = 'mcp-limite-debit' AND langue = 'it' AND position($t$ Può essere aumentato per test e verifiche, su richiesta.$t$ in texte) = 0;
-- La ligne française a changé : les autres langues s'y rattachent.
UPDATE traductions SET source_fr = (SELECT texte FROM traductions WHERE cle = 'mcp-limite-debit' AND langue = 'fr') WHERE cle = 'mcp-limite-debit';
UPDATE traductions SET texte = texte || $t$ Elle peut être relevée pour des tests et des audits, sur demande.$t$ WHERE cle = 'mcpc-cond-service' AND langue = 'fr' AND position($t$ Elle peut être relevée pour des tests et des audits, sur demande.$t$ in texte) = 0;
UPDATE traductions SET texte = texte || $t$ It can be raised for tests and audits, on request.$t$ WHERE cle = 'mcpc-cond-service' AND langue = 'en' AND position($t$ It can be raised for tests and audits, on request.$t$ in texte) = 0;
UPDATE traductions SET texte = texte || $t$ Für Tests und Prüfungen kann sie auf Anfrage angehoben werden.$t$ WHERE cle = 'mcpc-cond-service' AND langue = 'de' AND position($t$ Für Tests und Prüfungen kann sie auf Anfrage angehoben werden.$t$ in texte) = 0;
UPDATE traductions SET texte = texte || $t$ Può essere aumentato per test e verifiche, su richiesta.$t$ WHERE cle = 'mcpc-cond-service' AND langue = 'it' AND position($t$ Può essere aumentato per test e verifiche, su richiesta.$t$ in texte) = 0;
-- La ligne française a changé : les autres langues s'y rattachent.
UPDATE traductions SET source_fr = (SELECT texte FROM traductions WHERE cle = 'mcpc-cond-service' AND langue = 'fr') WHERE cle = 'mcpc-cond-service';

COMMIT;
