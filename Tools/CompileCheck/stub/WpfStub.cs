namespace System.Windows {
 public struct Point { public double X, Y; public Point(double x,double y){X=x;Y=y;} public static Vector operator -(Point a, Point b)=>new Vector(a.X-b.X,a.Y-b.Y); public static Point operator +(Point a, Vector v)=>new Point(a.X+v.X,a.Y+v.Y);}
 public struct Vector { public double X, Y; public Vector(double x,double y){X=x;Y=y;} public double Length=>Math.Sqrt(X*X+Y*Y); public static Vector operator *(Vector v,double d)=>new Vector(v.X*d,v.Y*d); public static Vector operator /(Vector v,double d)=>new Vector(v.X/d,v.Y/d); public void Normalize(){var l=Length; if(l>0){X/=l;Y/=l;}} }
}
namespace System.Windows.Threading {
 public class DispatcherTimer { public TimeSpan Interval {get;set;} public event EventHandler? Tick; public void Start(){} public void Stop(){} public bool IsEnabled {get;set;} }
}
public record BaanControleMelding(string Ernst, string Omschrijving);
