using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]

public class Block : MonoBehaviour
{
    Rigidbody2D rb;
    SpriteRenderer sr;
    Material mat;
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        sr = GetComponent<SpriteRenderer>();
        mat = sr.material; 
    }

    public enum Power
    {
        None,
        Jump,
        Right,
        Left
    }

    [SerializeField] Power blockPower;

    public void selectBlock()
    {
        mat.SetInt("_UseGlow", 1);
        //add glow effect when player selects this block
    }

    public void deselectBlock()
    {
        //get rid of glow effect
        mat.SetInt("_UseGlow", 0);
    }

    public void DestroyBlock()
    {
        //add particle effect here
        Destroy(gameObject);
    }

    void Start()
    {
        
    }

    
    void Update()
    {
        
    }
}
