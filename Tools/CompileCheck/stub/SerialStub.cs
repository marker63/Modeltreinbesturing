namespace System.IO.Ports;
public enum Parity { None, Odd, Even, Mark, Space }
public enum StopBits { None, One, Two, OnePointFive }
public class SerialDataReceivedEventArgs : EventArgs {}
public delegate void SerialDataReceivedEventHandler(object sender, SerialDataReceivedEventArgs e);
public class SerialPort : IDisposable {
  public SerialPort(string n, int b, Parity p, int d, StopBits s) {}
  public SerialPort() {}
  public static string[] GetPortNames() => new string[0];
  public bool IsOpen => false; public int BytesToRead => 0; public bool DtrEnable {get;set;} public bool RtsEnable {get;set;}
  public int ReadTimeout {get;set;} public int WriteTimeout {get;set;} public string PortName {get;set;}="" ; public int BaudRate {get;set;}
  public Parity Parity {get;set;} public int DataBits {get;set;} public StopBits StopBits {get;set;}
  public event SerialDataReceivedEventHandler? DataReceived;
  public void Open() {} public void Close() {} public void Dispose() {}
  public int Read(byte[] b,int o,int c)=>0; public void Write(byte[] b,int o,int c){} public void Write(string s){} public void DiscardInBuffer(){} public void DiscardOutBuffer(){}
  public string NewLine {get;set;}="\n"; public System.Text.Encoding Encoding {get;set;}=System.Text.Encoding.ASCII; public string ReadLine()=>""; public void WriteLine(string s){} public int ReadByte()=>0; public string ReadExisting()=>"";
}
