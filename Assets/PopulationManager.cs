using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.PlayerLoop;

public class PopulationManager : MonoBehaviour
{
    [Header("Champs UI (TMP)")]
    public TMP_InputField inputPopulation;
    public TMP_InputField inputPatrimoine;
    public TMP_InputField inputPatrimoineDur;   // % du patrimoine en “dur”

    public TMP_InputField inputGini;
    public Slider sliderGini;

    [Header("UI Panels")]
    public RectTransform panelCentiles;     // Panel HorizontalLayout pour 100 barres
    public RectTransform panelTranches;     // (prévu pour plus tard)

    [Header("Prefabs UI")]
    public GameObject prefabBarreCentile;   // Prefab: UI Image bleue + LayoutElement (width fixe)

    [Header("Aléatoire")]
    public bool deterministicSeed = true;
    public int seed = 12345;

    [Header("UI Stats (TMP_Text)")]
    public TMP_Text textPopulation;
    public TMP_Text textPatrimoineTotal;
    public TMP_Text textGini;
    public TMP_Text textTop1;
    public TMP_Text textTop10;
    public TMP_Text textMean;
    public TMP_Text textMedian;
    public TMP_Text textSeed;
    public TMP_Text textRichest;

    [Header("Croissance")]
    public TMP_InputField inputCroissance;          // % de croissance du patrimoine global
    public Toggle toggleCroissanceEgalitaire;       // mode de redistribution (true = égale, false = proportionnelle)
    public Slider sliderCroissance;

    [Header("Managers externes")]
    public BienVitauxManager bienVitauxManager;
    public ImpotManager impotManager;

    [Header("Barre Patrimoine")]
    public RectTransform panelPatrimoine;   // Panel vertical fixe
    public RectTransform segPatrimoineDur;
    public RectTransform segPatrimoineLiquide;
    public RectTransform segConsommation;
    public RectTransform segCroissance;
    public RectTransform segReste;
    public float patrimoineScale = 1f;
    public TMP_Text labelPatrimoineDur;
    public TMP_Text labelPatrimoineLiquide;
    public TMP_Text labelConsommation;
    public TMP_Text labelCroissance;

    [Header("Dette")]
    public RectTransform panelDette;   // Panel vertical dédié à la dette (ancré bas)
    public RectTransform segDette;     // Image/RectTransform de la barre dette
    public TMP_Text labelDette;        // Texte centré dans la barre pour afficher la valeur
    public float detteTotale = 0f;     // Dette cumulée
    
    [Header("Dette - Affichage cyclique")]
    public TMP_Text labelToursDette;    // Champ texte pour afficher le nombre de tours complets
    private int detteToursComplets = 0; // Compteur interne

    // Definition de la structure "Agent".
    [System.Serializable]
    public struct Agent
    {
        public float patrimoine;  // total (toujours = liquide + dur)
        public float liquide;      // part “cash”
        public float dur;          // part “dur”

        public Agent(float patrimoine, float partDur)
        {
            partDur = Mathf.Clamp01(partDur);
            this.patrimoine = patrimoine;
            this.dur = patrimoine * partDur;
            this.liquide = patrimoine - this.dur;
        }

        public void SyncTotal()
        {
            patrimoine = liquide + dur;
        }
    }


    // --- Internes existants ---
    private System.Random _rng;
    private readonly List<GameObject> _barresCentiles = new List<GameObject>();
    public float croissanceTaux { get; private set; } = 0f;
    public bool croissanceEgalitaire { get; private set; } = false;
    public float partPatrimoineDur { get; private set; } = 0f;

    // --- États persistés (source de vérité + agrégat UI) ---
    private Agent[] _agentsSorted;   // agents triés (pauvre -> riche)
    private float[] _centilesSums;   // 100 sommes par centile (pour l’UI)

    // Accès en lecture pour d’autres modules
    public IReadOnlyList<Agent> Agents => _agentsSorted;
    public IReadOnlyList<float> Centiles => _centilesSums;

    // ---------- Lifecycle ----------
    void Start()
    {
        // Init RNG
        _rng = new System.Random(deterministicSeed ? seed : Environment.TickCount);

        // Recalcul en direct quand les champs changent
        if (inputPopulation) inputPopulation.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());
        if (inputPatrimoine) inputPatrimoine.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());     
        if (inputPatrimoineDur) inputPatrimoineDur.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());
        if (sliderGini) sliderGini.onValueChanged.AddListener(OnSliderGiniChanged);
        if (inputCroissance) inputCroissance.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());
        if (toggleCroissanceEgalitaire) toggleCroissanceEgalitaire.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());

        if (inputGini) inputGini.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());
        if (inputCroissance) inputCroissance.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());
        if (toggleCroissanceEgalitaire) toggleCroissanceEgalitaire.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());
        if (inputPatrimoineDur) inputPatrimoineDur.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());

        if (sliderCroissance)
        {
            sliderCroissance.onValueChanged.AddListener(v =>
            {
                if (inputCroissance)
                    inputCroissance.SetTextWithoutNotify(v.ToString("0.0", CultureInfo.InvariantCulture));
                FindObjectOfType<StepManager>()?.MettreAJourSysteme();
            });

            // synchro input → slider
            if (inputCroissance)
            {
                var t = inputCroissance.text.Replace(',', '.');
                if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var cParsed))
                    sliderCroissance.SetValueWithoutNotify(cParsed);
            }
        }
    }











    // --- Simulation annuelle ---

    /// <summary>
    /// Applique les impôts à chaque agent : proportionnels au patrimoine.
    /// On retire d'abord sur la partie liquide, puis sur le patrimoine "dur" si besoin.
    /// </summary>
    public void AppliquerImpots(float totalImpots)
    {
        if (_agentsSorted == null || _agentsSorted.Length == 0) return;
        if (totalImpots <= 0f) return;

        var stats = GetTotals();
        float totalWealth = stats.totalWealth;
        if (totalWealth <= 0f) return;

        for (int i = 0; i < _agentsSorted.Length; i++)
        {
            float part = _agentsSorted[i].patrimoine / totalWealth;
            float impotAgent = totalImpots * part;

            if (impotAgent <= _agentsSorted[i].liquide)
            {
                _agentsSorted[i].liquide -= impotAgent;
            }
            else
            {
                float reste = impotAgent - _agentsSorted[i].liquide;
                _agentsSorted[i].liquide = 0f;
                _agentsSorted[i].dur = Mathf.Max(0f, _agentsSorted[i].dur - reste);
            }

            _agentsSorted[i].SyncTotal();
        }
    }


    /// <summary>
    /// Ajoute la croissance annuelle.
    /// Si égalitaire : chaque agent reçoit la même part.
    /// Sinon : proportionnellement au patrimoine.
    /// </summary>
    public void AppliquerCroissance()
    {
        if (_agentsSorted == null || _agentsSorted.Length == 0) return;
        if (croissanceTaux <= 0f) return;

        var stats = GetTotals();
        float totalWealth = stats.totalWealth;
        float delta = totalWealth * croissanceTaux;
        if (delta <= 0f) return;

        if (croissanceEgalitaire)
        {
            float gain = delta / _agentsSorted.Length;
            for (int i = 0; i < _agentsSorted.Length; i++)
            {
                _agentsSorted[i].liquide += gain;
                _agentsSorted[i].SyncTotal();
            }
        }
        else
        {
            for (int i = 0; i < _agentsSorted.Length; i++)
            {
                float part = _agentsSorted[i].patrimoine / totalWealth;
                _agentsSorted[i].liquide += delta * part;
                _agentsSorted[i].SyncTotal();
            }
        }
    }

    /// <summary>
    /// Ajoute le salaire épargné de l’année.
    /// Actuellement : redistribution proportionnelle au patrimoine.
    /// </summary>
    public void AjouterSalaireEpargne(float montantTotal)
    {
        if (_agentsSorted == null || _agentsSorted.Length == 0) return;
        if (montantTotal <= 0f) return;

        var stats = GetTotals();
        float totalWealth = stats.totalWealth;
        if (totalWealth <= 0f) return;

        for (int i = 0; i < _agentsSorted.Length; i++)
        {
            float part = _agentsSorted[i].patrimoine / totalWealth;
            _agentsSorted[i].liquide += montantTotal * part;
            _agentsSorted[i].SyncTotal();
        }
    }


    public void RecalculerCentiles()
    {
        if (_agentsSorted == null || _agentsSorted.Length == 0) return;

        // Recalcul des centiles
        _centilesSums = AggregateToCentiles(_agentsSorted, 100);

        // Trouver le max pour l’échelle
        float maxFortune = 0f;
        for (int i = 0; i < _centilesSums.Length; i++)
            if (_centilesSums[i] > maxFortune) maxFortune = _centilesSums[i];
        if (maxFortune <= 0f) maxFortune = 1f;

        // MAJ des barres existantes
        for (int i = 0; i < _barresCentiles.Count && i < _centilesSums.Length; i++)
        {
            var rt = _barresCentiles[i].GetComponent<RectTransform>();
            float hNorm = _centilesSums[i] / maxFortune;
            float hPix = Mathf.Max(0f, panelCentiles.rect.height * hNorm);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, hPix);
        }
    }











    //////////////////////////////////////////////
    ///
    /// Gestion de la DETTE
    /// 
    //////////////////////////////////////////////



    public void ConstruireBarreDette()
    {
        if (segDette == null || panelDette == null) return;

        float maxHeight = panelDette.rect.height;

        // Nombre de tours complets
        detteToursComplets = Mathf.FloorToInt((detteTotale * patrimoineScale) / maxHeight);

        // Hauteur du reste (entre 0 et maxHeight)
        float h = (detteTotale * patrimoineScale) % maxHeight;

        segDette.gameObject.SetActive(h > 0f);
        segDette.sizeDelta = new Vector2(segDette.sizeDelta.x, h);
        segDette.anchoredPosition = new Vector2(segDette.anchoredPosition.x, 0f);

        if (labelDette)
        {
            labelDette.gameObject.SetActive(h > 0f);
            labelDette.text = FormatAmount(detteTotale);
            //var rt = labelDette.rectTransform;
            //var pos = rt.anchoredPosition;
            //rt.anchoredPosition = new Vector2(pos.x, h * 0.5f);
        }

        if (labelToursDette)
        {
            labelToursDette.text = $"Tours: {detteToursComplets}";
            labelToursDette.gameObject.SetActive(detteToursComplets > 0);
        }
    }







    //////////////////////////////////////////////
    ///
    /// Gestion de la barre de patrimoine 
    /// 
    //////////////////////////////////////////////


    // --- Initialisation de la barre patrimoine ---
    public void InitialiserBarrePatrimoine()
    {
        if (panelPatrimoine == null) return;

        var stats = GetTotals();
        float total = stats.totalWealth;

        // Le patrimoine total (hors croissance) doit occuper 2/3 du panel
        float maxHeight = panelPatrimoine.rect.height;
        patrimoineScale = (maxHeight * (2f / 3f)) / Mathf.Max(1f, total);

        ConstruireBarrePatrimoine(); // premier dessin
    }


    // --- Mise à jour de la barre pendant la simulation ---
    public void MettreAJourBarrePatrimoine()
    {
        // Relire la croissance depuis l’UI en runtime, sans toucher à la scale
        if (inputCroissance)
        {
            var t = inputCroissance.text.Replace(',', '.');
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var cParsed))
                croissanceTaux = Mathf.Max(0f, cParsed / 100f); // ex: "2" → 0.02
        }
        if (toggleCroissanceEgalitaire)
            croissanceEgalitaire = toggleCroissanceEgalitaire.isOn;

        if (inputPatrimoineDur)
        {
            var t = inputPatrimoineDur.text.Replace(',', '.');
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                partPatrimoineDur = Mathf.Clamp01(parsed / 100f);
        }

        ConstruireBarrePatrimoine();

    }


    // --- Construction interne ---
    private void ConstruireBarrePatrimoine()
    {
        if (panelPatrimoine == null) return;

        var stats = GetTotals();

        // NOUVEAU
        float liquide = 0f, dur = 0f;
        if (_agentsSorted != null)
        {
            for (int i = 0; i < _agentsSorted.Length; i++)
            {
                liquide += _agentsSorted[i].liquide;
                dur += _agentsSorted[i].dur;
            }
        }
        float total = liquide + dur;


        float impot = (impotManager != null) ? impotManager.TotalImpots : 0f;


        float liquideAffiche = 0f;
        float durAffiche = 0f;
        float consoAffiche = 0f;

        
        float impotAffiche = 0f;

        if (impot <= 0f)
        {
            liquideAffiche = liquide;
            durAffiche = dur;
        }
        else if (impot < liquide)
        {
            liquideAffiche = liquide - impot;
            durAffiche = dur;
            impotAffiche = impot;
        }
        else if (impot < liquide + dur)
        {
            liquideAffiche = 0f;
            durAffiche = dur - (impot - liquide);
            impotAffiche = impot;
        }
        else
        {
            liquideAffiche = 0f;
            durAffiche = 0f;
            impotAffiche = total;
        }


        float croissance = total * croissanceTaux;
        float epargne = (bienVitauxManager != null) ? bienVitauxManager.SalaireEpargneActuel : 0f;

        // On additionne croissance + épargne
        float reinjection = croissance + epargne;

        // --- Affichage avec la scale figée ---
        float y = 0f;

        SetSegmentAndLabel(segPatrimoineDur, labelPatrimoineDur, durAffiche * patrimoineScale, ref y);
        SetSegmentAndLabel(segPatrimoineLiquide, labelPatrimoineLiquide, liquideAffiche * patrimoineScale, ref y);
        SetSegmentAndLabel(segConsommation, labelConsommation, impotAffiche * patrimoineScale, ref y);
        SetSegmentAndLabel(segCroissance, labelCroissance, reinjection * patrimoineScale, ref y);


        // Reste
        float maxHeight = panelPatrimoine.rect.height;
        float resteHeight = Mathf.Max(0f, maxHeight - y);
        SetSegment(segReste, resteHeight, ref y);

        ConstruireBarreDette();
    }

    private void SetSegmentAndLabel(RectTransform segment, TMP_Text label, float height, ref float y)
    {
        if (segment == null)
        {
            // même si le segment est null, on avance le curseur
            y += height;
            if (label) label.gameObject.SetActive(false);
            return;
        }

        bool visible = height > 0f;

        // Active/désactive le segment
        SetSegment(segment, height, ref y);

        // Labels : ON si visible, OFF si hauteur nulle
        if (label)
        {
            label.gameObject.SetActive(visible);
            if (visible)
            {
                // centre vertical du segment : startY = y - height
                float startY = y - height;
                var rt = label.rectTransform;
                var pos = rt.anchoredPosition;
                rt.anchoredPosition = new Vector2(pos.x, startY + height * 0.5f);
            }
        }
    }



    private void SetSegment(RectTransform rt, float height, ref float y)
    {
        if (rt == null) return;
        if (height <= 0f)
        {
            rt.gameObject.SetActive(false);
            return;
        }
        rt.gameObject.SetActive(true);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        y += height;
    }


















    //////////////////////////////////////////////
    ///
    /// CONSTRUIRE LA POPULATION
    /// 
    //////////////////////////////////////////////



    // ---------- Construction / UI ----------
    /// <summary> Lit les inputs, génère la population (agents), agrège en 100 centiles et dessine les barres. </summary>
    public void ConstruireCentiles()
    {
        // 1) Lire les valeurs UI (robuste: accepte "0.36" et "0,36")
        int population = 1000;
        float patrimoineTotal = 1_000_000f;
        float indiceGini = 0.3f;

        if (inputPopulation && int.TryParse(inputPopulation.text, out var popParsed))
            population = Mathf.Max(1, popParsed);

        if (inputPatrimoine)
        {
            var t = inputPatrimoine.text.Replace(',', '.');
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var patMoyenParsed))
            {
                float patrimoineMoyen = Mathf.Max(1e-6f, patMoyenParsed);
                patrimoineTotal = patrimoineMoyen * population;
            }
        }


        if (inputGini)
        {
            var t = inputGini.text.Replace(',', '.');
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var gParsed))
                indiceGini = Mathf.Clamp01(gParsed);
        }

        if (inputCroissance)
        {
            var t = inputCroissance.text.Replace(',', '.');
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var cParsed))
                croissanceTaux = Mathf.Max(0f, cParsed / 100f); // ex: "2" → 0.02
        }

        if (inputPatrimoineDur)
        {
            var t = inputPatrimoineDur.text.Replace(',', '.');
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                partPatrimoineDur = Mathf.Clamp01(parsed / 100f); // % → fraction
        }

        if (toggleCroissanceEgalitaire)
            croissanceEgalitaire = toggleCroissanceEgalitaire.isOn;

        // synchro slider -> input
        if (sliderGini)
        {
            sliderGini.SetValueWithoutNotify(indiceGini);
        }

        // 2) Générer richesses individuelles (log-normale calibrée par Gini)
        Agent[] agents = GenererPopulation(population, patrimoineTotal, indiceGini);

        // 3) Persister l’état "agents" (trié pauvre -> riche)
        _agentsSorted = agents;

        // 4) Agréger en 100 centiles (sommes par centile) et persister
        _centilesSums = AggregateToCentiles(_agentsSorted, 100);

        // 5) Trouver le max pour l’échelle (UI)
        float maxFortune = 0f;
        for (int i = 0; i < _centilesSums.Length; i++)
            if (_centilesSums[i] > maxFortune) maxFortune = _centilesSums[i];
        if (maxFortune <= 0f) maxFortune = 1f;

        // 6) Détruire l’existant puis instancier les 100 barres
        foreach (var go in _barresCentiles) Destroy(go);
        _barresCentiles.Clear();

        for (int i = 0; i < _centilesSums.Length; i++)
        {
            var barre = Instantiate(prefabBarreCentile, panelCentiles);
            var rt = barre.GetComponent<RectTransform>();

            float hNorm = _centilesSums[i] / maxFortune;   // 0..1
            float hPix = Mathf.Max(0f, panelCentiles.rect.height * hNorm);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, hPix); // largeur = LayoutElement

            _barresCentiles.Add(barre);
        }

        // 7) MAJ HUD stats
        UpdateStatsUI();

    }










    private float[] AggregateToCentiles(Agent[] agentsSorted, int nb = 100)
    {
        int n = agentsSorted?.Length ?? 0;
        nb = Mathf.Max(1, nb);

        var outSums = new float[nb];
        if (n == 0) return outSums;

        for (int i = 0; i < nb; i++)
        {
            int start = Mathf.FloorToInt(i * (n / (float)nb));
            int end = Mathf.FloorToInt((i + 1) * (n / (float)nb));
            start = Mathf.Clamp(start, 0, n);
            end = Mathf.Clamp(end, 0, n);

            double s = 0;
            for (int j = start; j < end; j++) s += agentsSorted[j].patrimoine;
            outSums[i] = (float)s;
        }
        return outSums;
    }

    // ---------- Stats / HUD ----------
    [Serializable]
    public struct PopulationStats
    {
        public int n;
        public int seed;
        public float totalWealth;
        public float gini;        // 0..1
        public float top1Share;   // 0..1 (part du patrimoine détenu par le top 1%)
        public float top10Share;  // 0..1
        public float mean;
        public float median;
    }

    // ---------- Slider / Input sync ----------
    public void OnSliderGiniChanged(float value)
    {
        if (inputGini)
            inputGini.SetTextWithoutNotify(
                value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            );
        FindObjectOfType<StepManager>()?.ReinitialiserSysteme();
    }








    /// <summary> Calcule les principaux agrégats sur la population triée. </summary>
    public PopulationStats GetTotals()
    {
        var stats = new PopulationStats { n = _agentsSorted?.Length ?? 0, seed = seed };
        if (stats.n <= 0) return stats;

        double sum = 0;
        for (int i = 0; i < stats.n; i++) sum += _agentsSorted[i].patrimoine;
        stats.totalWealth = (float)sum;

        stats.mean = stats.totalWealth / Mathf.Max(1, stats.n);
        stats.median = ComputeMedianFromSorted(_agentsSorted);

        stats.gini = ComputeGiniFromSorted(_agentsSorted);

        // Top 1% / 10% shares
        int k1 = Mathf.Max(1, Mathf.CeilToInt(0.01f * stats.n));
        int k10 = Mathf.Max(1, Mathf.CeilToInt(0.10f * stats.n));

        double sumTop1 = 0, sumTop10 = 0;
        for (int i = stats.n - k1; i < stats.n; i++) sumTop1 += _agentsSorted[i].patrimoine;
        for (int i = stats.n - k10; i < stats.n; i++) sumTop10 += _agentsSorted[i].patrimoine;

        if (sum > 0)
        {
            stats.top1Share = (float)(sumTop1 / sum);
            stats.top10Share = (float)(sumTop10 / sum);
        }
        else
        {
            stats.top1Share = stats.top10Share = 0f;
        }
        return stats;
    }




    /// <summary> Met à jour les TMP_Text publics si assignés. </summary>
    public void UpdateStatsUI()
    {
        var s = GetTotals();

        if (textPopulation) textPopulation.text = s.n.ToString("N0", CultureInfo.InvariantCulture);
        if (textPatrimoineTotal) textPatrimoineTotal.text = FormatAmount(s.totalWealth);
        if (textGini) textGini.text = s.gini.ToString("0.00", CultureInfo.InvariantCulture);
        if (textTop1) textTop1.text = (s.top1Share).ToString("P1", CultureInfo.InvariantCulture);
        if (textTop10) textTop10.text = (s.top10Share).ToString("P1", CultureInfo.InvariantCulture);
        if (textMean) textMean.text = FormatAmount(s.mean);
        if (textMedian) textMedian.text = FormatAmount(s.median);
        if (textSeed) textSeed.text = s.seed.ToString();
        if (_agentsSorted != null && _agentsSorted.Length > 0)
        {
            float richest = _agentsSorted[_agentsSorted.Length - 1].patrimoine;
            if (textRichest) textRichest.text = FormatAmount(richest);
        }
    }





    private static float ComputeMedianFromSorted(Agent[] sorted)
    {
        int n = sorted.Length;
        if (n == 0) return 0f;
        if ((n & 1) == 1) return sorted[n / 2].patrimoine;
        return 0.5f * (sorted[n / 2 - 1].patrimoine + sorted[n / 2].patrimoine);
    }









    private static float ComputeGiniFromSorted(Agent[] sorted)
    {
        int n = sorted.Length;
        if (n == 0) return 0f;

        double sum = 0, cum = 0;
        for (int i = 0; i < n; i++) sum += sorted[i].patrimoine;
        if (sum <= 0) return 0f;

        for (int i = 0; i < n; i++) cum += (i + 1) * sorted[i].patrimoine; // i=0..n-1 -> poids 1..n
        double g = (2.0 * cum) / (n * sum) - (n + 1.0) / n;
        if (g < 0) g = 0;
        if (g > 1) g = 1;
        return (float)g;
    }

    private static string FormatAmount(float v)
    {
        float av = Mathf.Abs(v);
        if (av >= 1_000_000_000f) return (v / 1_000_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + " Md";
        if (av >= 1_000_000f) return (v / 1_000_000f).ToString("0.##", CultureInfo.InvariantCulture) + " M";
        if (av >= 1_000f) return (v / 1_000f).ToString("0.##", CultureInfo.InvariantCulture) + " k";
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // ---------- Génération aléatoire calibrée Gini ----------
    private Agent[] GenererPopulation(int n, float total, float gini)
    {
        n = Mathf.Max(1, n);
        total = Mathf.Max(1e-6f, total);

        // Convertit Gini -> sigma (calibration exacte)
        float G = Mathf.Clamp01(gini);
        if (G > 0.98f) G = 0.98f; // stabilité
        double p = (G + 1.0) * 0.5;
        double sigma = Math.Sqrt(2.0) * InvNorm(p);
        double mu = -0.5 * sigma * sigma;

        var r = _rng ?? (_rng = new System.Random(seed));
        float[] vals = new float[n];
        double sum = 0.0;

        for (int i = 0; i < n; i++)
        {
            double u1 = 1.0 - r.NextDouble();
            double u2 = 1.0 - r.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            double ln = Math.Exp(mu + sigma * z);
            vals[i] = (float)ln;
            sum += ln;
        }

        float k = total / (float)sum;
        for (int i = 0; i < n; i++) vals[i] *= k;

        Array.Sort(vals);

        Agent[] agents = new Agent[n];
        for (int i = 0; i < n; i++)
            agents[i] = new Agent(vals[i], partPatrimoineDur);
        return agents;
    }

    private static double InvNorm(double p)
    {
        if (p <= 0.0) return double.NegativeInfinity;
        if (p >= 1.0) return double.PositiveInfinity;

        double[] a = { -3.969683028665376e+01,  2.209460984245205e+02, -2.759285104469687e+02,
                        1.383577518672690e+02, -3.066479806614716e+01,  2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01,  1.615858368580409e+02, -1.556989798598866e+02,
                        6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
                       -2.549732539343734e+00,  4.374664141464968e+00,  2.938163982698783e+00 };
        double[] d = {  7.784695709041462e-03,  3.224671290700398e-01,  2.445134137142996e+00,
                        3.754408661907416e+00 };

        double pl = 0.02425;
        double pu = 1.0 - pl;

        if (p < pl)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }
        if (p > pu)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                      ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }

        double q2 = p - 0.5;
        double r = q2 * q2;
        return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q2 /
               (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1.0);
    }

    /// <summary> Bouton UI: génère un nouveau seed et reconstruit (nouvelle population). </summary>
    public void Reseed()
    {
        // 1) Nouveau seed + RNG
        seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        _rng = new System.Random(seed);

        // 2) Laisser StepManager refaire tout le pipeline proprement
        var step = FindObjectOfType<StepManager>();
        step?.ReinitialiserSysteme();
    }
}
