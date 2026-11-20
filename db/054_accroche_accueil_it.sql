-- Accroche de la page d'accueil raccourcie (« Chaque point est un élu. Pour chaque texte, » retiré) : nouvelle clé française, traduction italienne. ON CONFLICT DO NOTHING.
BEGIN;
INSERT INTO traductions (langue, cle, texte) VALUES
 ('it', $t$Voyez en un coup d'œil qui a voté <strong class="c-pour">pour</strong>, <strong class="c-contre">contre</strong>, s'est <strong class="c-abstention">abstenu</strong>, ou n'a pas pris part au vote (<strong class="c-absent">absent</strong>).$t$,
        $t$Vedete a colpo d'occhio chi ha votato <strong class="c-pour">a favore</strong>, <strong class="c-contre">contro</strong>, si è <strong class="c-abstention">astenuto</strong>, o non ha preso parte al voto (<strong class="c-absent">assente</strong>).$t$)
ON CONFLICT (langue, cle) DO NOTHING;
COMMIT;
