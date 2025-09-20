using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BienVitauxManager : MonoBehaviour
{
    [Header("Références UI")]
    public TMP_InputField inputPourcentageBeneficiaires;   // %
    public TMP_InputField inputCoutParPersonne;            // coût annuel pour une personne
    public TMP_InputField inputPourcentageImport;          // %
    public TMP_InputField inputPourcentageUsure;           // %
    public TMP_InputField inputPourcentageSalaire;         // %
    public TMP_InputField inputTauxEpargne;                // %
    public Toggle toggleCorrectionBalance;                 // double import si coché

    [Header("Panneaux / Barres")]
    public RectTransform panelCout;       // barre verticale pour le coût vitaux

    [Header("Segments de la barre coût")]
    public RectTransform segSalaireConsome;
    public RectTransform segSalaireEpargne;
    public RectTransform segUsure;
    public RectTransform segImport;
    public RectTransform segExport;

    [Header("Composition Export (mini-barres)")]
    public RectTransform segBalanceSalaire;
    public RectTransform segBalanceUsure;
    public RectTransform segBalanceImport;

    [Header("Managers")]
    public PopulationManager populationManager;
    public ImpotManager impotManager;
    public float SalaireEpargneActuel { get; private set; } = 0f;

    [Header("Réglage d’affichage")]
    public float facteurVisibilite = 1f; 

    // --- Points de départ (fixés une fois) ---
    private float pointDeDepartSalaire;
    private float pointDeDepartUsure;
    private float pointDeDepartImport;

    // --- Facteurs d’évolution (modifiables dans le temps) ---
    [Header("Multiplicateurs runtime (crises)")]
    public Slider sliderMultSalaire;
    public Slider sliderMultUsure;
    public Slider sliderMultImport;
    public TMP_Text labelMultSalaire; // optionnel (affiche ×1.20)
    public TMP_Text labelMultUsure;   // optionnel
    public TMP_Text labelMultImport;  // optionnel
    public TMP_Text labelSalaireConsome;
    public TMP_Text labelSalaireEpargne;
    public TMP_Text labelUsure;
    public TMP_Text labelImport;
    public TMP_Text labelExport;
    public float facteurSalaire = 1f;
    public float facteurUsure = 1f;
    public float facteurImport = 1f;
    public float exportSalairePart;
    public float exportUsurePart;
    public float exportImportPart;

    





    private bool _lockUpdate = false;
    public float CoutTotalActuel { get; private set; } = 0f;

    void Start()
    {
        // Base (re-init)
        if (inputPourcentageBeneficiaires) inputPourcentageBeneficiaires.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());
        if (inputCoutParPersonne) inputCoutParPersonne.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());
        if (inputPourcentageSalaire) inputPourcentageSalaire.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());
        if (inputPourcentageUsure) inputPourcentageUsure.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());
        if (inputPourcentageImport) inputPourcentageImport.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.ReinitialiserSysteme());

        if (sliderMultSalaire)
        {
            // Range conseillé dans l’Inspector : min=0.2, max=3 (facultatif)
            sliderMultSalaire.SetValueWithoutNotify(Mathf.Clamp(facteurSalaire, sliderMultSalaire.minValue, sliderMultSalaire.maxValue));
            sliderMultSalaire.onValueChanged.AddListener(v =>
            {
                facteurSalaire = Mathf.Max(0f, v);
                UpdateMultLabels();
                FindObjectOfType<StepManager>()?.MettreAJourSysteme();
            });
        }

        if (sliderMultUsure)
        {
            sliderMultUsure.SetValueWithoutNotify(Mathf.Clamp(facteurUsure, sliderMultUsure.minValue, sliderMultUsure.maxValue));
            sliderMultUsure.onValueChanged.AddListener(v =>
            {
                facteurUsure = Mathf.Max(0f, v);
                UpdateMultLabels();
                FindObjectOfType<StepManager>()?.MettreAJourSysteme();
            });
        }

        if (sliderMultImport)
        {
            sliderMultImport.SetValueWithoutNotify(Mathf.Clamp(facteurImport, sliderMultImport.minValue, sliderMultImport.maxValue));
            sliderMultImport.onValueChanged.AddListener(v =>
            {
                facteurImport = Mathf.Max(0f, v);
                UpdateMultLabels();
                FindObjectOfType<StepManager>()?.MettreAJourSysteme();
            });
        }

        if (inputTauxEpargne)
            inputTauxEpargne.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());
        if (toggleCorrectionBalance)
            toggleCorrectionBalance.onValueChanged.AddListener(_ => FindObjectOfType<StepManager>()?.MettreAJourSysteme());

        // Label init
        UpdateMultLabels();

    }

    // --- 1) Initialise les points de départ à partir des % ---
    public void InitialiserPointsDeDepart()
    {
        if (populationManager == null) return;

        var stats = populationManager.GetTotals();
        float population = stats.n;

        float pctBenef = ParsePercent(inputPourcentageBeneficiaires, 1f);
        float coutParPers = ParseFloat(inputCoutParPersonne, 1000f);
        float pctImport = ParsePercent(inputPourcentageImport, 0f);
        float pctUsure = ParsePercent(inputPourcentageUsure, 0f);
        float pctSalaire = ParsePercent(inputPourcentageSalaire, 1f);

        // Calcul du coût global
        float nbBeneficiaires = population * pctBenef;
        float coutTotal = nbBeneficiaires * coutParPers;

        // Décomposition initiale en valeurs absolues
        pointDeDepartSalaire = coutTotal * pctSalaire;
        pointDeDepartUsure = coutTotal * pctUsure;
        pointDeDepartImport = coutTotal * pctImport;
    }

    // --- 2) Met à jour la barre avec les valeurs évoluées ---
    public void MettreAJourCout()
    {
        // --- Calcul toujours fait ---
        float salaire = pointDeDepartSalaire * facteurSalaire;
        float usure = pointDeDepartUsure * facteurUsure;
        float import = pointDeDepartImport * facteurImport;

        // Équilibrage de la balance : on doit "produire" l'équivalent des importations.
        float export = 0f;
        if (toggleCorrectionBalance && toggleCorrectionBalance.isOn)
        {
            float pS = ParsePercent(inputPourcentageSalaire, 1f);
            float pU = ParsePercent(inputPourcentageUsure, 0f);
            float pI = ParsePercent(inputPourcentageImport, 0f);
            float norm = Mathf.Max(1e-6f, pS + pU + pI);
            pS /= norm; pU /= norm; pI /= norm;

            float exportSalaire = import * (pS * facteurSalaire);
            float exportUsure = import * (pU * facteurUsure);
            float exportImport = import * (pI * facteurImport);

            exportSalairePart = exportSalaire;
            exportUsurePart = exportUsure;
            exportImportPart = exportImport;

            export = exportSalaire + exportUsure + exportImport;
        }
        else
        {
            exportSalairePart = exportUsurePart = exportImportPart = 0.0f;
        }

        float tauxEpargne = ParsePercent(inputTauxEpargne, 0f);

        // Épargne domestique (affichée dans la barre "salaire")
        float salaireEpargneDomestique = salaire * tauxEpargne;
        float salaireConsome = salaire - salaireEpargneDomestique;

        // Épargne sur les salaires liés aux exports (cachée ici, mais injectée plus tard)
        float salaireEpargneExport = exportSalairePart * tauxEpargne;

        // Total pour réinjection dans le patrimoine
        SalaireEpargneActuel = salaireEpargneDomestique + salaireEpargneExport;

        float total = salaire + usure + import + export;
        CoutTotalActuel = total;

        // Affichage du label Export
        bool showExportLabel = (toggleCorrectionBalance && toggleCorrectionBalance.isOn && export > 0f);
        if (labelExport) labelExport.gameObject.SetActive(showExportLabel);

        // --- Dessin UI ---
        if (panelCout == null) return;

        float patrimoineScale = (populationManager != null) ? populationManager.patrimoineScale : 0f;
        float zoomImpots = (impotManager != null) ? Mathf.Max(1e-6f, impotManager.facteurZoom) : 1f;
        float scale = Mathf.Max(0f, patrimoineScale) * zoomImpots;

        float y = 0f;
        // Ici on affiche uniquement l’épargne domestique
        SetSegmentAndLabel(segSalaireEpargne, labelSalaireEpargne, salaireEpargneDomestique * scale, ref y);
        SetSegmentAndLabel(segSalaireConsome, labelSalaireConsome, salaireConsome * scale, ref y);
        SetSegmentAndLabel(segUsure, labelUsure, usure * scale, ref y);
        SetSegmentAndLabel(segImport, labelImport, import * scale, ref y);
        float exportStartY = y;
        SetSegmentAndLabel(segExport, labelExport, export * scale, ref y);

        // Mini-barres exports
        float hES = exportSalairePart * scale;
        float hEU = exportUsurePart * scale;
        float hEI = exportImportPart * scale;

        bool showMini = (toggleCorrectionBalance && toggleCorrectionBalance.isOn && export > 0f);

        if (!showMini)
        {
            if (segBalanceSalaire) segBalanceSalaire.gameObject.SetActive(false);
            if (segBalanceUsure) segBalanceUsure.gameObject.SetActive(false);
            if (segBalanceImport) segBalanceImport.gameObject.SetActive(false);
        }
        else
        {
            float yComp = exportStartY;
            SetSideBar(segBalanceSalaire, hES, yComp); yComp += hES;
            SetSideBar(segBalanceUsure, hEU, yComp); yComp += hEU;
            SetSideBar(segBalanceImport, hEI, yComp);
        }
    }


    private void SetSideBar(RectTransform rt, float height, float yBottom)
    {
        if (!rt) return;
        if (height <= 0f)
        {
            rt.gameObject.SetActive(false);
            return;
        }
        rt.gameObject.SetActive(true);
        // On ne touche pas à X/width/anchors (tu fixes ça dans l’Inspector)
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        var pos = rt.anchoredPosition;
        rt.anchoredPosition = new Vector2(pos.x, yBottom);
    }


    private void SetSegmentAndLabel(RectTransform segment, TMP_Text label, float height, ref float y)
    {
        // height est déjà "affiché" (valeur × scale) ⇒ on peut décider l’affichage du label ici
        bool visible = height > 0f;

        // Label ON/OFF selon la hauteur
        if (label) label.gameObject.SetActive(visible);

        // Pose/masque le segment comme d'habitude
        float startY = y;                // bas du segment avant incrément
        SetSegment(segment, height, ref y);

        // Centre le label si visible
        if (visible && label != null)
        {
            var rt = label.rectTransform;
            var pos = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(pos.x, startY + height * 0.5f);
        }
    }


    private void SetSegment(RectTransform rt, float height, ref float y)
    {
        if (rt == null) return;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        y += height;
    }

    // --- Gestion des 3 inputs liés (Salaire/Usure/Import) ---
    private void OnPourcentageChanged(string source)
    {
        if (_lockUpdate) return;
        _lockUpdate = true;

        float salaire = ParsePercent(inputPourcentageSalaire, 0f);
        float usure = ParsePercent(inputPourcentageUsure, 0f);
        float import = ParsePercent(inputPourcentageImport, 0f);

        switch (source)
        {
            case "Salaire": Repartir(ref usure, ref import, 100f - salaire); break;
            case "Usure": Repartir(ref salaire, ref import, 100f - usure); break;
            case "Import": Repartir(ref salaire, ref usure, 100f - import); break;
        }

        inputPourcentageSalaire.SetTextWithoutNotify(Mathf.RoundToInt(salaire).ToString());
        inputPourcentageUsure.SetTextWithoutNotify(Mathf.RoundToInt(usure).ToString());
        inputPourcentageImport.SetTextWithoutNotify(Mathf.RoundToInt(import).ToString());

        _lockUpdate = false;
    }

    private void Repartir(ref float a, ref float b, float reste)
    {
        if (reste <= 0f) { a = b = 0f; return; }
        if (a == 0f && b == 0f) { a = b = reste * 0.5f; }
        else if (a == 0f) b = reste;
        else if (b == 0f) a = reste;
        else
        {
            float somme = a + b;
            a = reste * (a / somme);
            b = reste * (b / somme);
        }
    }

    private static float ParseFloat(TMP_InputField field, float def)
    {
        if (!field) return def;
        var t = field.text.Replace(',', '.');
        if (float.TryParse(t, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var f))
            return f;
        return def;
    }

    public static float ParsePercent(TMP_InputField field, float def01)
    {
        float v = ParseFloat(field, def01 * 100f);
        return Mathf.Clamp01(v / 100f);
    }

    private void UpdateMultLabels()
    {
        if (labelMultSalaire) labelMultSalaire.text = "×" + facteurSalaire.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        if (labelMultUsure) labelMultUsure.text = "×" + facteurUsure.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        if (labelMultImport) labelMultImport.text = "×" + facteurImport.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }


}
