using UnityEngine;
using UnityEngine.UIElements;

// Illustration vectorielle provisoire, indépendante de la résolution et des assets de gameplay.
public class GuildMapView : VisualElement
{
    public GuildMapView()
    {
        AddToClassList("map-art");
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        if (contentRect.width < 1f || contentRect.height < 1f) return;
        var p = context.painter2D;
        Vector2 Point(float x, float y) => new(x * contentRect.width / 620f, y * contentRect.height / 400f);
        void Polygon(string color, params Vector2[] points)
        {
            ColorUtility.TryParseHtmlString(color, out var fill);
            p.fillColor = fill; p.BeginPath(); p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath(); p.Fill();
        }
        void Shape(string color, params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = Point(xy[i * 2], xy[i * 2 + 1]);
            Polygon(color, points);
        }
        void Tree(float x, float y, float size)
        {
            Shape("#31473a", x - 3, y, x + 3, y, x + 3, y + 24, x - 3, y + 24);
            Shape("#315a48", x, y - size, x + size * .7f, y + 5, x, y + size * .4f, x - size * .7f, y + 5);
            Shape("#3c6b51", x, y - size, x, y + size * .4f, x - size * .7f, y + 5);
        }
        Shape("#223b33", 30, 222, 300, 60, 590, 218, 315, 365);
        Shape("#2f493a", 30, 210, 300, 48, 590, 206, 315, 352);
        Shape("#53604b", 60, 230, 303, 88, 558, 230, 315, 357);
        Shape("#778068", 257, 118, 289, 100, 386, 157, 350, 177);
        Shape("#69745b", 288, 156, 330, 132, 470, 218, 428, 242);
        Shape("#69745b", 165, 225, 200, 206, 354, 297, 318, 319);
        Tree(79, 185, 38); Tree(121, 115, 29); Tree(179, 100, 24);
        Tree(486, 133, 34); Tree(545, 182, 28); Tree(476, 275, 24);
        // Forge : murs de pierre, toiture de tuiles et foyer allumé.
        Shape("#202f2c", 96, 219, 194, 275, 289, 224, 189, 168);
        Shape("#9c977e", 101, 161, 190, 205, 190, 259, 101, 213);
        Shape("#6a7162", 190, 205, 270, 160, 270, 215, 190, 259);
        Shape("#b97251", 86, 163, 170, 104, 205, 151, 193, 210);
        Shape("#8e4e3c", 170, 104, 249, 111, 284, 162, 193, 210, 205, 151);
        Shape("#614736", 110, 173, 123, 179, 123, 220, 110, 214);
        Shape("#524638", 165, 199, 180, 207, 180, 254, 165, 246);
        Shape("#222e29", 213, 200, 247, 181, 247, 226, 213, 245);
        Shape("#e59647", 220, 214, 241, 201, 241, 226, 220, 238);
        Shape("#f5c577", 225, 220, 235, 211, 239, 228, 225, 236);
        Shape("#757d70", 222, 100, 243, 111, 243, 153, 222, 142);
        Shape("#a5a68b", 207, 111, 222, 100, 222, 142, 207, 153);
        Shape("#566c62", 219, 84, 231, 75, 242, 82, 231, 91);
        Shape("#405950", 230, 64, 248, 55, 262, 62, 245, 72);
        // Marché : charpente ouverte et auvent rayé.
        Shape("#29372e", 351, 221, 432, 268, 527, 217, 445, 174);
        Shape("#c7af80", 359, 155, 370, 160, 370, 226, 359, 220);
        Shape("#8f7756", 493, 159, 503, 153, 503, 219, 493, 225);
        Shape("#8f7756", 420, 197, 430, 203, 430, 264, 420, 258);
        Shape("#c2a274", 347, 158, 427, 112, 519, 158, 427, 207);
        Shape("#739d8b", 347, 158, 366, 147, 448, 196, 427, 207);
        Shape("#739d8b", 390, 134, 409, 123, 492, 172, 471, 183);
        Shape("#487967", 427, 112, 445, 121, 370, 166, 347, 158);
        Shape("#487967", 466, 132, 485, 141, 409, 186, 389, 176);
        Shape("#e5c38b", 375, 209, 423, 236, 480, 205, 433, 179);
        Shape("#9c7c54", 375, 209, 423, 236, 423, 253, 375, 226);
        Shape("#6f694c", 423, 236, 480, 205, 480, 223, 423, 253);
        Shape("#c68d58", 390, 200, 404, 192, 418, 201, 404, 209);
        Shape("#8eaa73", 426, 196, 440, 188, 454, 197, 440, 205);
        Shape("#b29a75", 297, 241, 317, 229, 338, 241, 317, 252);
        Shape("#7c8469", 297, 241, 317, 252, 317, 275, 297, 264);
        Shape("#525d48", 317, 252, 338, 241, 338, 264, 317, 275);
    }
}
