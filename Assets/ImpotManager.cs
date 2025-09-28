using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ImpotManager : MonoBehaviour
{
    [Header("Références UI")]
    public RectTransform panelCentiles;  // panel parent des centiles bleus
    public RectTransform tranche1;
    public RectTransform tranche2;
    public RectTransform tranche3;
    public RectTransform tranche4;
    public RectTransform tranche5;

    [Header("Inputs Seuils (borne max de chaque tranche)")]
    public TMP_InputField inputSeuil1;
    public TMP_InputField inputSeuil2;
    public TMP_InputField inputSeuil3;
    public TMP_InputField inputSeuil4;
    public TMP_InputField inputSeuil5;

    [Header("Inputs Taux (pourcentage par tranche)")]
    public TMP_InputField inputTaux1;
    public TMP_InputField inputTaux2;
    public TMP_InputField inputTaux3;
    public TMP_InputField inputTaux4;
    public TMP_InputField inputTaux5;

    [Header("UI Preset de Taux")]
    public TMP_Dropdown dropdownPresets;

    [Header("Auto recalcul annuel")]
    public Toggle toggleAutoTranches;  // Recalcul auto des tranches
    public Toggle toggleAutoTaux;      // Recalcul auto des taux

    [Header("Managers")]
    public PopulationManager populationManager;
    public BienVitauxManager bienVitauxManager;
    public float TotalImpots { get; private set; } = 0f;
    public float facteurVisibilite = 1f;

    [Header("Barre Impôts")]
    public RectTransform panelImpots;
    public RectTransform segImpotTranche1;
    public RectTransform segImpotTranche2;
    public RectTransform segImpotTranche3;
    public RectTransform segImpotTranche4;
    public RectTransform segImpotTranche5;

    [Header("Retour au patrimoine (images)")]
    public RectTransform imgRetourEpargne;     
    public RectTransform imgRetourCroissance;  

    [Header("Zoom")]
    public TMP_Dropdown dropdownZoom;  // optionnel
    public float facteurZoom = 1f;     // 1× par défaut
    private readonly float[] _zoomOptions = { 1f, 5f, 10f, 20f, 40f };

    private bool _autoLock = false;


    // preset
    [System.Serializable]
    public class ImpotPreset
    {
        public string name;
        public int[] rates;
    }
    [System.Serializable]
    public class ImpotPresetCollection
    {
        public ImpotPreset[] presets;
    }
    private ImpotPresetCollection loadedPresets;


    // Valeurs numériques utilisées dans le calcul
    private float[] seuils = new float[5];
    private float[] taux = new float[5];
    private float[] _impotsParTranche = new float[5];

    void Start()
    {
        // Toggling → simple redraw runtime
        if (toggleAutoTranches) toggleAutoTranches.onValueChanged.AddListener(_ =>
            FindObjectOfType<StepManager>()?.MettreAJourSysteme());
        if (toggleAutoTaux) toggleAutoTaux.onValueChanged.AddListener(_ =>
            FindObjectOfType<StepManager>()?.MettreAJourSysteme());

        // Quand on modifie un SEUIL → peut déclencher tranches + taux
        if (inputSeuil1) inputSeuil1.onValueChanged.AddListener(OnSeuilChanged);
        if (inputSeuil2) inputSeuil2.onValueChanged.AddListener(OnSeuilChanged);
        if (inputSeuil3) inputSeuil3.onValueChanged.AddListener(OnSeuilChanged);
        if (inputSeuil4) inputSeuil4.onValueChanged.AddListener(OnSeuilChanged);
        if (inputSeuil5) inputSeuil5.onValueChanged.AddListener(OnSeuilChanged);

        // Quand on modifie un TAUX → peut déclencher taux
        if (inputTaux1) inputTaux1.onValueChanged.AddListener(OnTauxChanged);
        if (inputTaux2) inputTaux2.onValueChanged.AddListener(OnTauxChanged);
        if (inputTaux3) inputTaux3.onValueChanged.AddListener(OnTauxChanged);
        if (inputTaux4) inputTaux4.onValueChanged.AddListener(OnTauxChanged);
        if (inputTaux5) inputTaux5.onValueChanged.AddListener(OnTauxChanged);

        if (dropdownPresets)
            dropdownPresets.onValueChanged.AddListener(OnPresetSelected);

        if (dropdownZoom)
        {
            InitZoomDropdown();                      // remplit si vide
            dropdownZoom.onValueChanged.AddListener(OnZoomChanged);

            // >>> Forcer le 20× au lancement
            // Essaie d’abord de trouver "40×", sinon prend la dernière option.
            int idx20 = dropdownZoom.options.FindIndex(o => o.text.Trim().StartsWith("40"));
            if (idx20 < 0) idx20 = Mathf.Max(0, dropdownZoom.options.Count - 1);
            dropdownZoom.SetValueWithoutNotify(idx20);
            OnZoomChanged(idx20); // applique facteurZoom + redraw runtime
        }
        else
        {
            // Pas de dropdown → on part quand même à 20×
            facteurZoom = 20f;
        }


        SetupRectTransform(tranche1);
        SetupRectTransform(tranche2);
        SetupRectTransform(tranche3);
        SetupRectTransform(tranche4);
        SetupRectTransform(tranche5);

        LoadPresetsFromJson();
    }

    private void OnSeuilChanged(string _)
    {
        if (_autoLock) return;

        // Recalc des tranches si demandé
        if (toggleAutoTranches && toggleAutoTranches.isOn)
        {
            _autoLock = true;
            AutoCalculerTranches();   // SetTextWithoutNotify → pas de boucle
            _autoLock = false;
        }

        // Recalc des taux si demandé (après tranches)
        if (toggleAutoTaux && toggleAutoTaux.isOn)
        {
            _autoLock = true;
            RecalculerTauxAutomatique(); // cette méthode fait le redraw runtime
            _autoLock = false;
        }
        // sinon au moins un redraw
        if (!(toggleAutoTaux && toggleAutoTaux.isOn))
            FindObjectOfType<StepManager>()?.MettreAJourSysteme();
    }

    private void OnTauxChanged(string _)
    {
        if (_autoLock) return;

        if (toggleAutoTaux && toggleAutoTaux.isOn)
        {
            _autoLock = true;
            RecalculerTauxAutomatique();
            _autoLock = false;
        }
        else
        {
            FindObjectOfType<StepManager>()?.MettreAJourSysteme();
        }
    }

    


    public void CalculerTotalImpots()
    {
        TotalImpots = 0f;
        if (populationManager == null) return;
        var agents = populationManager.Agents;
        if (agents == null || agents.Count == 0) return;

        foreach (var agent in agents)
        {
            float patrimoine = agent.patrimoine;
            float impot = CalculerImpotsPourValeur(patrimoine);
            TotalImpots += impot;
        }

        Debug.Log($"[ImpotManager] Total des impôts collectés = {TotalImpots}");
    }

    /// <summary> Calcule la somme totale et la répartit par tranches. </summary>
    /// <summary>
    /// Calcule la somme totale et la répartit marginalement par tranches.
    /// </summary>
    public void CalculerImpotsDetail()
    {
        // reset
        for (int i = 0; i < _impotsParTranche.Length; i++) _impotsParTranche[i] = 0f;
        TotalImpots = 0f;

        if (populationManager == null) return;
        var agents = populationManager.Agents;
        if (agents == null || agents.Count == 0) return;

        foreach (var agent in agents)
        {
            float valeur = agent.patrimoine;
            if (valeur <= 0f) continue;

            float prevMax = 0f;

            for (int i = 0; i < seuils.Length; i++)
            {
                // borne haute de la tranche i
                float borne = (i < seuils.Length - 1) ? seuils[i] : float.PositiveInfinity;

                // portion taxable dans cette tranche
                float taxable = Mathf.Clamp(valeur - prevMax, 0f, borne - prevMax);
                if (taxable <= 0f) { prevMax = borne; continue; }

                float imp = taxable * (taux[i] * 0.01f);
                _impotsParTranche[i] += imp;
                TotalImpots += imp;

                prevMax = borne;

                // si on a épuisé la valeur de l’agent, on arrête
                if (valeur <= borne) break;
            }
        }
    }

    public float CalculerImpotsAgent(float patrimoine)
    {
        return CalculerImpotsPourValeur(patrimoine); // méthode privée existante
    }


    private float CalculerImpotsPourValeur(float valeur)
    {
        if (valeur <= 0f) return 0f;

        float montant = 0f;
        float prevMax = 0f;

        for (int i = 0; i < seuils.Length; i++)
        {
            float borne = (i < seuils.Length - 1) ? seuils[i] : float.PositiveInfinity;

            float taxable = Mathf.Clamp(valeur - prevMax, 0f, borne - prevMax);
            if (taxable <= 0f) { prevMax = borne; continue; }

            montant += taxable * (taux[i] * 0.01f);

            prevMax = borne;
            if (valeur <= borne) break;
        }
        return montant;
    }




    /// <summary> Met à jour la barre verticale d’impôts. </summary>
    public void MettreAJourBarreImpots()
    {
        if (panelImpots == null || populationManager == null) return;

        float scale = populationManager.patrimoineScale * Mathf.Max(1e-6f, facteurZoom);

        // --- Impôts par tranche (basique et robuste) ---
        float y = 0f;
        for (int i = 0; i < _impotsParTranche.Length; i++)
        {
            float h = _impotsParTranche[i] * scale;
            var rt = GetImpotSegmentRT(i);

            if (rt == null) continue;

            if (h > 0f)
            {
                rt.gameObject.SetActive(true);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
                y += h;
            }
            else
            {
                rt.gameObject.SetActive(false);
            }
        }

        // --- Retour au patrimoine (toujours affiché) ---
        float epargne = (bienVitauxManager != null) ? bienVitauxManager.SalaireEpargneActuel : 0f;
        var totals = populationManager.GetTotals();
        float croissance = totals.totalWealth * populationManager.croissanceTaux;

        float yRetour = 0f;
        SetSegment(imgRetourEpargne, epargne * scale, ref yRetour);
        SetSegment(imgRetourCroissance, croissance * scale, ref yRetour);

        if (imgRetourEpargne) imgRetourEpargne.gameObject.SetActive(epargne > 0f);
        if (imgRetourCroissance) imgRetourCroissance.gameObject.SetActive(croissance > 0f);
    }

    private RectTransform GetImpotSegmentRT(int i)
    {
        switch (i)
        {
            case 0: return segImpotTranche1;
            case 1: return segImpotTranche2;
            case 2: return segImpotTranche3;
            case 3: return segImpotTranche4;
            case 4: return segImpotTranche5;
            default: return null;
        }
    }





    private void SetSegment(RectTransform rt, float height, ref float y)
    {
        if (rt == null) return;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        y += height;
    }

    



    public void UpdateTranchesUI()
    {
        if (populationManager == null || panelCentiles == null) return;
        var agents = populationManager.Agents;
        var centiles = populationManager.Centiles;
        if (agents == null || agents.Count == 0 || centiles == null || centiles.Count == 0) return;

        // 1) Lire les seuils depuis les champs TMP
        seuils[0] = ParseInput(inputSeuil1, float.PositiveInfinity);
        seuils[1] = ParseInput(inputSeuil2, float.PositiveInfinity);
        seuils[2] = ParseInput(inputSeuil3, float.PositiveInfinity);
        seuils[3] = ParseInput(inputSeuil4, float.PositiveInfinity);
        seuils[4] = ParseInput(inputSeuil5, float.PositiveInfinity);

        // 2) Lire les taux depuis les champs TMP
        taux[0] = ParseInput(inputTaux1, 0f);
        taux[1] = ParseInput(inputTaux2, 0f);
        taux[2] = ParseInput(inputTaux3, 0f);
        taux[3] = ParseInput(inputTaux4, 0f);
        taux[4] = ParseInput(inputTaux5, 0f);

        int n = agents.Count;
        int nbCentiles = centiles.Count;
        float totalWidth = panelCentiles.rect.width;

        int[] count = new int[seuils.Length];

        for (int i = 0; i < nbCentiles; i++)
        {
            int start = Mathf.FloorToInt(i * (n / (float)nbCentiles));
            int end = Mathf.FloorToInt((i + 1) * (n / (float)nbCentiles)) - 1;
            start = Mathf.Clamp(start, 0, n - 1);
            end = Mathf.Clamp(end, start, n - 1);

            int mid = (start + end) >> 1;
            float w = agents[mid].patrimoine;

            int t = TrouverTranche(w);
            count[t]++;
        }

        // 3) Mettre à jour largeur + position des rectangles
        float x = 0f;
        for (int t = 0; t < seuils.Length; t++)
        {
            float frac = count[t] / (float)nbCentiles;
            float width = frac * totalWidth;

            RectTransform rt = GetTrancheRT(t);
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            }
            x += width;
        }
    }

    public void AutoCalculerTranches()
    {
        if (populationManager == null) return;
        var agents = populationManager.Agents;
        if (agents == null || agents.Count == 0) return;

        int n = agents.Count;
        int centiles = 100;

        // Découpage en nombre de centiles
        int[] trancheCentiles = { 40, 30, 15, 12, 3 };

        float[] seuilsAuto = new float[5];
        int cumul = 0;

        for (int t = 0; t < trancheCentiles.Length; t++)
        {
            cumul += trancheCentiles[t];
            int endCentile = Mathf.Min(cumul, centiles) - 1; // index du centile max de la tranche

            // convertir centile → index dans agents
            int endIndex = Mathf.FloorToInt((endCentile + 1) * (n / (float)centiles)) - 1;
            endIndex = Mathf.Clamp(endIndex, 0, n - 1);

            seuilsAuto[t] = agents[endIndex].patrimoine;
        }

        // Mettre les valeurs dans les InputFields (sans déclencher les listeners)
        if (inputSeuil1) inputSeuil1.SetTextWithoutNotify(seuilsAuto[0].ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (inputSeuil2) inputSeuil2.SetTextWithoutNotify(seuilsAuto[1].ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (inputSeuil3) inputSeuil3.SetTextWithoutNotify(seuilsAuto[2].ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (inputSeuil4) inputSeuil4.SetTextWithoutNotify(seuilsAuto[3].ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (inputSeuil5) inputSeuil5.SetTextWithoutNotify(seuilsAuto[4].ToString("0", System.Globalization.CultureInfo.InvariantCulture));

    }


    /// <summary>
    /// Recalcule automatiquement les taux par tranche pour couvrir exactement
    /// le coût de production des biens vitaux, en doublant après la première.
    /// Règle: M = [0, 1, 2, 4, 8] (si 5 tranches), taux = s * M * 100
    /// avec s choisi pour que Σ_i (taux_i% * base_i) = CoutTotalActuel.
    /// </summary>
    public void RecalculerTauxAutomatique()
    {
        if (populationManager == null || bienVitauxManager == null) return;

        // 1) S’assure que les seuils/taux internes sont synchronisés avec l’UI
        UpdateTranchesUI(); // lit seuils[] depuis les inputs

        // 2) Base taxable par tranche (en valeur monétaire), indépendante des taux
        float[] bases = CalculerBasesParTranche(); // longueur = seuils.Length

        // 3) Poids géométriques: 0,1,2,4,8... (première tranche à 0)
        int n = bases.Length;
        float[] M = new float[n];
        M[0] = 0f;
        for (int i = 1; i < n; i++) M[i] = Mathf.Pow(2f, i - 1);

        // 4) Résout s dans: total = Σ ( (s*M[i]) * bases[i] )
        double denom = 0.0;
        for (int i = 0; i < n; i++) denom += M[i] * bases[i];

        float target = bienVitauxManager.CoutTotalActuel;
        float s = (denom > 1e-6) ? (target / (float)denom) : 0f;

        // 5) Compose les taux (%) et pousse dans l’UI (arrondi entier pour rester lisible)
        int[] newRates = new int[n];
        for (int i = 0; i < n; i++)
        {
            float r = Mathf.Max(0f, s * M[i] * 100f); // en %
            newRates[i] = Mathf.RoundToInt(Mathf.Min(r, 100f));
        }
        SetTaux(newRates); // met à jour les 5 champs sans déclencher de listeners

    }
    /// <summary>
    /// Calcule la base taxable cumulée par tranche (en monnaie), sans appliquer les taux.
    /// Utilise les bornes de tranche (seuils) et, pour la dernière tranche, prend tout le reste.
    /// </summary>
    private float[] CalculerBasesParTranche()
    {
        int n = seuils.Length;
        float[] bases = new float[n];

        if (populationManager == null) return bases;
        var agents = populationManager.Agents;
        if (agents == null || agents.Count == 0) return bases;

        foreach (var agent in agents)
        {
            float valeur = agent.patrimoine;
            float prevMax = 0f;

            for (int i = 0; i < n; i++)
            {
                // Borne haute de la tranche i : seuil[i] pour 0..n-2, "infini" borné à la valeur de l'agent pour la dernière
                float upper = (i < n - 1) ? seuils[i] : valeur;

                float span = Mathf.Max(0f, upper - prevMax);
                if (span <= 0f) { prevMax = upper; continue; }

                float abovePrev = Mathf.Max(0f, valeur - prevMax);
                float taxable = Mathf.Min(abovePrev, span);

                if (taxable > 0f) bases[i] += taxable;

                prevMax = upper;
                if (valeur <= upper) break;
            }
        }

        return bases;
    }





    private int TrouverTranche(float valeur)
    {
        for (int i = 0; i < seuils.Length; i++)
            if (valeur <= seuils[i]) return i;
        return seuils.Length - 1;
    }

    private RectTransform GetTrancheRT(int i)
    {
        switch (i)
        {
            case 0: return tranche1;
            case 1: return tranche2;
            case 2: return tranche3;
            case 3: return tranche4;
            case 4: return tranche5;
            default: return null;
        }
    }

    private static void SetupRectTransform(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
        rt.anchorMax = new Vector2(0f, rt.anchorMax.y);
        rt.pivot = new Vector2(0f, rt.pivot.y);
    }

    private static float ParseInput(TMP_InputField field, float defaultValue)
    {
        if (field == null) return defaultValue;
        var t = field.text.Replace(',', '.');
        if (float.TryParse(t, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return defaultValue;
    }

    private void LoadPresetsFromJson()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "presets_impot.json");
        if (!System.IO.File.Exists(path)) return;

        string json = System.IO.File.ReadAllText(path);
        loadedPresets = JsonUtility.FromJson<ImpotPresetCollection>(json);

        if (dropdownPresets && loadedPresets != null && loadedPresets.presets.Length > 0)
        {
            dropdownPresets.ClearOptions();
            var options = new System.Collections.Generic.List<string>();
            foreach (var preset in loadedPresets.presets)
                options.Add(preset.name);
            dropdownPresets.AddOptions(options);
            dropdownPresets.SetValueWithoutNotify(2);
        }
    }


    public void OnPresetSelected(int index)
    {
        if (loadedPresets == null || loadedPresets.presets == null) return;
        if (index < 0 || index >= loadedPresets.presets.Length) return;

        var preset = loadedPresets.presets[index];
        //Debug.Log($"[ImpotManager] Preset sélectionné: {preset.name} (index {index})");

        SetTaux(preset.rates);

        // Recalcule tout le système (population, barres, total impôts avec log)
        FindObjectOfType<StepManager>()?.ReinitialiserSysteme();
    }



    private void SetTaux(int[] values)
    {
        if (inputTaux1) { inputTaux1.SetTextWithoutNotify(values[0].ToString()); }
        if (inputTaux2) { inputTaux2.SetTextWithoutNotify(values[1].ToString()); }
        if (inputTaux3) { inputTaux3.SetTextWithoutNotify(values[2].ToString()); }
        if (inputTaux4) { inputTaux4.SetTextWithoutNotify(values[3].ToString()); }
        if (inputTaux5) { inputTaux5.SetTextWithoutNotify(values[4].ToString()); }
    }


    private void InitZoomDropdown()
    {
        if (dropdownZoom.options == null || dropdownZoom.options.Count == 0)
        {
            var opts = new System.Collections.Generic.List<string>();
            foreach (var z in _zoomOptions) opts.Add($"{z}×");
            dropdownZoom.AddOptions(opts);
            dropdownZoom.value = 0; // 1×
            dropdownZoom.RefreshShownValue();
        }
        // synchroniser facteurZoom si l’UI a déjà des options personnalisées
        int i = Mathf.Clamp(dropdownZoom.value, 0, _zoomOptions.Length - 1);
        facteurZoom = _zoomOptions[i];
    }

    private void OnZoomChanged(int index)
    {
        index = Mathf.Clamp(index, 0, _zoomOptions.Length - 1);
        facteurZoom = _zoomOptions[index];

        var step = FindObjectOfType<StepManager>();
        if (step != null) step.MettreAJourSysteme();  // ← pas ReinitialiserSysteme()
        else MettreAJourBarreImpots();
    }

    // --- Boutons UI : appellent les recalculs "purs" puis déclenchent un update global ---

    public void UI_CalculAutoTranches()
    {
        AutoCalculerTranches(); // ← ta fonction existante, SANS appel StepManager
        var step = FindObjectOfType<StepManager>();
        step?.MettreAJourSysteme(); // redessine impôts + patrimoine à l’échelle courante
    }

    public void UI_CalculAutoTaux()
    {
        RecalculerTauxAutomatique(); // ← ta fonction existante, SANS appel StepManager
        var step = FindObjectOfType<StepManager>();
        step?.MettreAJourSysteme();
    }




}
