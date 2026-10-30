using UnityEngine;
using UnityEngine.UI;

namespace DemonWeave.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DialogueOptionGlyph : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var center = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .45f;
            const int segments = 40;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                int first = mesh.currentVertCount;
                foreach (var p in new[] { center + new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius, center + new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,
                    center + new Vector2(Mathf.Cos(b),Mathf.Sin(b))*(radius-1.5f), center + new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(radius-1.5f) }) mesh.AddVert(p, color, Vector2.zero);
                mesh.AddTriangle(first,first+1,first+2); mesh.AddTriangle(first,first+2,first+3);
            }
            void Bar(float x, float y, float w, float h)
            {
                int first = mesh.currentVertCount;
                foreach (var p in new[] { new Vector2(x,y), new Vector2(x+w,y), new Vector2(x+w,y+h), new Vector2(x,y+h) }) mesh.AddVert(center+p,color,Vector2.zero);
                mesh.AddTriangle(first,first+1,first+2); mesh.AddTriangle(first,first+2,first+3);
            }
            Bar(-radius*.42f, radius*.24f, radius*.84f, 1.8f);
            Bar(-radius*.42f, -radius*.05f, radius*.62f, 1.8f);
            Bar(-radius*.42f, -radius*.34f, radius*.42f, 1.8f);
        }
    }
}
