// TooltipManager.cs (remplace juste les parties indiquées)
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TooltipManager : MonoBehaviour
{
    public static TooltipManager Instance;

    [Header("Références")]
    public Canvas canvas;               // Ton Canvas principal (celui qui affiche l’UI)
    public RectTransform racineTooltip; // Panel du tooltip (Image + Layout)
    public TMP_Text texteTooltip;

    [Header("Comportement")]
    public Vector2 decalagePixels = new Vector2(16f, -16f);
    public float margeEcran = 8f;
    public bool suitCurseur = true;
    [Tooltip("Ordre de tri du Canvas interne au tooltip")]
    public int ordreDeTriTooltip = 5000;

    bool visible;
    Canvas _tooltipCanvas;  // << ajouté

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // S'assurer que le tooltip a son propre Canvas au-dessus de tout
        if (racineTooltip)
        {
            _tooltipCanvas = racineTooltip.GetComponent<Canvas>();
            if (_tooltipCanvas == null) _tooltipCanvas = racineTooltip.gameObject.AddComponent<Canvas>();
            _tooltipCanvas.overrideSorting = true;
            _tooltipCanvas.sortingOrder = ordreDeTriTooltip;
            if (racineTooltip.GetComponent<GraphicRaycaster>() == null)
                racineTooltip.gameObject.AddComponent<GraphicRaycaster>();
        }
        Cacher();
    }

    void Update()
    {
        if (visible && suitCurseur) PositionnerSuivantCurseur();
    }

    public void Afficher(string texte)
    {
        if (!racineTooltip) return;
        if (texteTooltip) texteTooltip.text = texte;

        LayoutRebuilder.ForceRebuildLayoutImmediate(racineTooltip);

        MettreDevant();

        visible = true;
        racineTooltip.gameObject.SetActive(true);
        PositionnerSuivantCurseur();
    }

    public void Cacher()
    {
        visible = false;
        if (racineTooltip) racineTooltip.gameObject.SetActive(false);
    }

    public void MettreDevant()
    {
        if (!canvas || !racineTooltip) return;

        // 1) s’assurer qu’il est directement sous le Canvas principal (pas dans le panel des barres)
        if (racineTooltip.parent != canvas.transform)
            racineTooltip.SetParent(canvas.transform, false);

        // 2) en dernier sibling (dans ce Canvas)
        racineTooltip.SetAsLastSibling();

        // 3) Canvas dédié sur la racine + override sorting très haut
        var rootCanvas = racineTooltip.GetComponent<Canvas>();
        if (!rootCanvas) rootCanvas = racineTooltip.gameObject.AddComponent<Canvas>();
        rootCanvas.overrideSorting = true;
        rootCanvas.sortingLayerID = canvas.sortingLayerID;   // même layer que l’UI
        rootCanvas.sortingOrder = 10000;                   // très haut

        // 4) uniformiser tous les Canvas enfants (ex: si le TMP_Text a son propre Canvas)
        var childCanvases = racineTooltip.GetComponentsInChildren<Canvas>(true);
        foreach (var c in childCanvases)
        {
            c.overrideSorting = true;
            c.sortingLayerID = rootCanvas.sortingLayerID;
            c.sortingOrder = rootCanvas.sortingOrder;
        }
    }

    void PositionnerSuivantCurseur()
    {
        if (!racineTooltip) return;

        Vector2 souris = Input.mousePosition;
        float screenW = Screen.width;
        float screenH = Screen.height;

        float offset = 20f;
        float largeur = racineTooltip.rect.width;
        float hauteur = racineTooltip.rect.height;

        float x, y;

        // Horizontal
        if (souris.x > screenW * 0.5f)
            x = souris.x - (largeur + offset) * (1f - racineTooltip.pivot.x);
        else
            x = souris.x + offset - largeur * racineTooltip.pivot.x;

        // Vertical
        if (souris.y > screenH * 0.5f)
            y = souris.y - (hauteur + offset) * (1f - racineTooltip.pivot.y);
        else
            y = souris.y + offset - hauteur * racineTooltip.pivot.y;

        racineTooltip.position = new Vector2(x, y);
    }





}
