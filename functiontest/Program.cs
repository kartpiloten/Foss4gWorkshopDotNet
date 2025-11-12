

int _percentage = 75;

if (_percentage <= 0) Console.WriteLine(" 0 "); ;
if (_percentage >= 100)
{
    // Full circle
    Console.WriteLine("M 100,20 A 80,80 0 1,1 99.99,20 Z");
}

// Calculate end point of arc
var angle = (_percentage / 100.0) * 2 * Math.PI;
var endX = 100 + 80 * Math.Sin(angle);
var endY = 100 - 80 * Math.Cos(angle);

// Large arc flag if more than 50%
var largeArc = _percentage > 50 ? 1 : 0;

Console.WriteLine($"M 100,100 L 100,20 A 80,80 0 {largeArc},1 {endX:F2},{endY:F2} Z");

