using UnityEngine;

public class StepManager : MonoBehaviour
{
    [Header("Managers")]
    public PopulationManager populationManager;
    public BienVitauxManager bienVitauxManager;
    public ImpotManager impotManager;

    bool _isUpdating = false;

    void Start()
    {
        ReinitialiserSysteme();
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
        }
        finally { _isUpdating = false; }
    }





}
