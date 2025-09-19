// TooltipDeclencheur.cs
using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipDeclencheur : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea] public string message;
    public float delaiApparition = 0f;

    bool pointeurDessus;
    bool affiche;
    float tEntree;

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointeurDessus = true;
        affiche = false;
        tEntree = Time.unscaledTime;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointeurDessus = false;
        affiche = false;
        if (TooltipManager.Instance) TooltipManager.Instance.Cacher();
    }

    void Update()
    {
        if (!pointeurDessus || TooltipManager.Instance == null || affiche) return;

        if (Time.unscaledTime - tEntree >= delaiApparition)
        {
            TooltipManager.Instance.Afficher(message);
            affiche = true;
        }
    }

    void OnDisable()
    {
        pointeurDessus = false;
        affiche = false;
        if (TooltipManager.Instance) TooltipManager.Instance.Cacher();
    }
}
