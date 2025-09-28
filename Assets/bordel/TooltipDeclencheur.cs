using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipDeclencheur : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea]
    public string message;          // texte fixe pour les cas normaux
    public int indexCentile = -1;   // >=0 si cette barre correspond à un centile

    public float delaiApparition = 0f;

    private bool pointeurDessus = false;
    private bool affiche = false;
    private float tEntree = 0f;

    void Update()
    {
        if (!pointeurDessus || TooltipManager.Instance == null || affiche) return;

        UnityEngine.Debug.Log($"Update Centile {indexCentile} - affiche:{affiche} délai:{Time.unscaledTime - tEntree >= delaiApparition}");


        if (Time.unscaledTime - tEntree >= delaiApparition)
        {
            string texteAAfficher = message;

            if (indexCentile >= 0)
            {
                // On va chercher les infos dynamiques du centile
                PopulationManager pop = FindObjectOfType<PopulationManager>();
                ImpotManager impots = FindObjectOfType<ImpotManager>();
                UnityEngine.Debug.Log("Tentative affichage tooltip centile...");

                if (pop != null && impots != null)
                {
                    // Patrimoine moyen du centile
                    float patrimoineMoyen = pop.Centiles[indexCentile] / (pop.Agents.Count / 100f);

                    // % croissance reçue
                    float croissancePct = pop.GetCroissancePourCentile(indexCentile) * 100f;

                    float partCroissancePct = pop.GetPartCroissancePourCentile(indexCentile) * 100f;

                    // % impôt payé
                    float impotPct = 0f;
                    if (patrimoineMoyen > 0f)
                    {
                        float impot = impots.CalculerImpotsAgent(patrimoineMoyen);
                        impotPct = (impot / patrimoineMoyen) * 100f;
                    }

                    texteAAfficher =
                        $"Centile {indexCentile + 1}\n" +
                        $"Patrimoine moyen : {patrimoineMoyen:0.##}\n" +
                        $"Croissance reçue : {croissancePct:0.0}%\n" +
                        $"Part de la croissance totale : {partCroissancePct:0.0}%\n" +
                        $"Impôt payé : {impotPct:0.0}%";
                }
            }
            UnityEngine.Debug.Log($"Appel Afficher avec: {texteAAfficher}");
            TooltipManager.Instance.Afficher(texteAAfficher);
            affiche = true;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        //Debug.Log($"ENTER - Centile {indexCentile}");
        pointeurDessus = true;
        affiche = false;
        tEntree = Time.unscaledTime;

    }

    public void OnPointerExit(PointerEventData eventData)
    {
        UnityEngine.Debug.Log($"EXIT - Centile {indexCentile} - affiche était: {affiche}");
        pointeurDessus = false;
        affiche = false;
        if (TooltipManager.Instance != null)
            TooltipManager.Instance.Cacher();
    }
}
