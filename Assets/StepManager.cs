using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class StepManager : MonoBehaviour
{
    [Header("Managers")]
    public PopulationManager populationManager;
    public BienVitauxManager bienVitauxManager;
    public ImpotManager impotManager;

    [Header("Temps qui passe")]
    public TMP_Text labelAnneesPassees;           // champ texte pour afficher l'année
    public Slider sliderVitesseTemps;             // slider pour régler la vitesse (1–10)
    private int anneesPassees = 0;                // compteur interne
    public TMP_Text labelTempusBouton;            // <<< label du bouton Tempus Fugit
    private Coroutine tempusCoroutine;            // référence à la coroutine en cours

    bool _isUpdating = false;


    void Awake()
    {
        Screen.SetResolution(1280, 720, false); // false = fenêtré
    }



    void Start()
    {
        ReinitialiserSysteme();
        MettreAJourLabelAnnees();
    }


    // La boucle annuelle
    private void BoucleSurUnAn()
    {
        // 1) Incrémenter le compteur d’années
        anneesPassees++;
        MettreAJourLabelAnnees();

        // 2) Appliquer les impôts (répartis proportionnellement)
        populationManager.AppliquerImpots(impotManager.TotalImpots);

        // 3) Appliquer la croissance (égale ou proportionnelle)
        populationManager.AppliquerCroissance();

        // 4) Ajouter le salaire épargné de l’année (domestique + salaires liés aux exports)
        float tauxEpargne = BienVitauxManager.ParsePercent(bienVitauxManager.inputTauxEpargne, 0f);
        float totalEpargne = bienVitauxManager.SalaireEpargneActuel
                               + (bienVitauxManager.exportSalairePart * tauxEpargne);
        populationManager.AjouterSalaireEpargne(totalEpargne);
        

        // Dette : si le coût de production > impôts, on s'endette ; sinon on rembourse jusqu'à 0
        float delta = bienVitauxManager.CoutTotalActuel - impotManager.TotalImpots;

        if (delta > 0f)
            populationManager.detteTotale += delta; // on creuse la dette
        else
            populationManager.detteTotale = Mathf.Max(0f, populationManager.detteTotale + delta); // delta < 0 => on rembourse
        
        

        // 5) Recalcul auto des tranches/taux si demandé
        if (impotManager.toggleAutoTranches && impotManager.toggleAutoTranches.isOn)
            impotManager.AutoCalculerTranches();

        if (impotManager.toggleAutoTaux && impotManager.toggleAutoTaux.isOn)
            impotManager.RecalculerTauxAutomatique();

        // 8) Recalculer les impôts détaillés avec le nouveau patrimoine
        impotManager.UpdateTranchesUI();
        impotManager.CalculerImpotsDetail();

        // 7) Recalculer et afficher les stats
        populationManager.RecalculerCentiles();
        populationManager.UpdateStatsUI();

        // 6) Mettre à jour l’UI et les calculs finaux
        MettreAJourSysteme();

    }






    // Fonction appelée par bouton "+1 an"
    public void BoutonPlusUnAn()
    {
        BoucleSurUnAn();
    }

    // Fonction appelée par bouton "+10 ans"
    public void BoutonPlusDixAns()
    {
        if (tempusCoroutine == null)   // évite de superposer plusieurs coroutines
            tempusCoroutine = StartCoroutine(PlusDixAnsRoutine());
    }

    private IEnumerator PlusDixAnsRoutine()
    {
        for (int i = 0; i < 10; i++)
        {
            BoucleSurUnAn();

            // lire vitesse (boucles par seconde) depuis le slider
            float vitesse = (sliderVitesseTemps != null) ? sliderVitesseTemps.value : 1f;
            float delay = 1f / Mathf.Max(0.0001f, vitesse);

            yield return new WaitForSeconds(delay);
        }

        tempusCoroutine = null; // libérer la référence pour permettre un nouvel appel
    }

    // Fonction appelée par bouton "tempus fugit" (toggle on/off)
    public void BoutonTempusFugit()
    {
        if (tempusCoroutine == null)
        {
            // lancement
            tempusCoroutine = StartCoroutine(TempusFugitRoutine());
            if (labelTempusBouton) labelTempusBouton.text = "Pause";
        }
        else
        {
            // arrêt
            StopCoroutine(tempusCoroutine);
            tempusCoroutine = null;
            if (labelTempusBouton) labelTempusBouton.text = "Play";
        }
    }


    private IEnumerator TempusFugitRoutine()
    {
        while (true)
        {
            // lire vitesse (boucles par seconde) depuis le slider
            float vitesse = (sliderVitesseTemps != null) ? sliderVitesseTemps.value : 1f;
            float delay = 1f / Mathf.Max(0.0001f, vitesse);

            BoucleSurUnAn();

            yield return new WaitForSeconds(delay);
        }
    }

    private void MettreAJourLabelAnnees()
    {
        if (labelAnneesPassees)
            labelAnneesPassees.text = anneesPassees.ToString();
    }





    // INIT (calcule et fige patrimoineScale)
    // Cette function sert à construire l'état intial du système. 
    // On la lance quand on change des paramètre de départ
    // Mais surtout pas pendant le runtime.
    public void ReinitialiserSysteme()
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            if (populationManager == null) return;

            // 1) Population → agents/centiles
            populationManager.ConstruireCentiles();

            // 2) Biens vitaux → points de départ
            if (bienVitauxManager != null)
                bienVitauxManager.InitialiserPointsDeDepart();

            // 3) Lire l’UI impôts (pur)
            if (impotManager != null)
                impotManager.UpdateTranchesUI();

            // 4) Fixer la scale patrimoine (une fois)
            populationManager.InitialiserBarrePatrimoine();
            // Remise à zéro de la dette
            if (populationManager != null)
                populationManager.detteTotale = 0f;

            // 5) Biens vitaux → coût courant (cible pour l’auto-taux)
            if (bienVitauxManager != null)
                bienVitauxManager.MettreAJourCout();

            // 6) Recalculs impôts explicites si demandé
            if (impotManager != null)
            {
                if (impotManager.toggleAutoTranches && impotManager.toggleAutoTranches.isOn)
                    impotManager.AutoCalculerTranches();

                if (impotManager.toggleAutoTaux && impotManager.toggleAutoTaux.isOn)
                    impotManager.RecalculerTauxAutomatique();

                // 7) Relire UI et recalculer montants finaux
                impotManager.UpdateTranchesUI();
                impotManager.CalculerImpotsDetail();
            }

            // 8) Dessins finaux
            populationManager.MettreAJourBarrePatrimoine();
            if (impotManager != null)
                impotManager.MettreAJourBarreImpots();

            // 10) Reinitialiser le compteur d'année passée.
            anneesPassees = 0;
            MettreAJourLabelAnnees();

        }
        finally { _isUpdating = false; }
    }



    // UPDATE (ne recalcule pas la scale)
    // Ca, c'est la function qu'on appelle à chaque tick pour mettre à jour toute l'UI
    // Il faut l'appeler une fois par cycle, après avoir recalculé ce qui doit l'être.
    public void MettreAJourSysteme()
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            if (populationManager == null) return;

            // 1) Lire l’UI impôts (pur, sans redraw)
            if (impotManager != null)
                impotManager.UpdateTranchesUI();

            // 2) Biens vitaux → calc coût courant (sert de cible au recalcul de taux)
            if (bienVitauxManager != null)
                bienVitauxManager.MettreAJourCout();

            // 3) Recalculs impôts explicites si demandé (pas d’appel à StepManager côté ImpotManager)
            if (impotManager != null)
            {
                if (impotManager.toggleAutoTranches && impotManager.toggleAutoTranches.isOn)
                    impotManager.AutoCalculerTranches();       // modifie les champs UI via SetTextWithoutNotify

                if (impotManager.toggleAutoTaux && impotManager.toggleAutoTaux.isOn)
                    impotManager.RecalculerTauxAutomatique();  // idem
            }

            // 4) Relire l’UI impôts après auto (les champs ont pu bouger), puis recalculer les montants
            if (impotManager != null)
            {
                impotManager.UpdateTranchesUI();
                impotManager.CalculerImpotsDetail();
            }

            // 5) Dessins finaux (scale patrimoine figée)
            populationManager.MettreAJourBarrePatrimoine();
            if (impotManager != null)
                impotManager.MettreAJourBarreImpots();

            populationManager.ConstruireBarreDette();
        }
        finally { _isUpdating = false; }
    }





}
