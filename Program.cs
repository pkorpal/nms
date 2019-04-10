using System;
using System.Collections.Generic;
using System.IO;
using System.Net;  
using System.Net.Sockets;  
using System.Text;  
using System.Threading; 
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
  
// State object for reading client data asynchronously  

public class StateObject {  
    public Socket workSocket = null;  
    public const int BufferSize = 1024;    
    public byte[] buffer = new byte[BufferSize];  
    public StringBuilder sb = new StringBuilder();    
}  
  
public class DeviceSettings {
    public class Connection {
        [JsonProperty("portOut")]
        public int portOut { get; set;}
        [JsonProperty("label")]
        public int label { get; set;}
    }
    
    [JsonProperty("name")]
    public String name { get; set; }
    [JsonProperty("port")]
    public int port { get; set; }
    [JsonProperty("changed")]
    public bool changed { get; set; }
    [JsonProperty("connections")]
    public Connection[] connections { get; set;}
    [JsonProperty("active")]
    public bool active { get; set;}
}

public class NetworkDevice {
    public String name {get; set;}
    public String endpoint {get;set;}
}

public class KeepAliveMessage {
    public String name {get; set;}

    public String endpoint {get; set;}
}

public class AsynchronousSocketListener {   
    public static ManualResetEvent allDone = new ManualResetEvent(false);  
  
    public AsynchronousSocketListener() {  
    }  
  
    public static async System.Threading.Tasks.Task StartListeningAsync() {  
        IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());  
        IPAddress ipAddress = ipHostInfo.AddressList[0];  
        IPEndPoint localEndPoint = new IPEndPoint(ipAddress, getNMSport());  
  
        Socket listener = new Socket(ipAddress.AddressFamily,  
            SocketType.Stream, ProtocolType.Tcp );  
   
        try {  
            listener.Bind(localEndPoint);  
            listener.Listen(100);  
            while (true) {  
                allDone.Reset();  
                try {
                    DeviceSettings[] device_settings = checkConfigFile();

                    int num_of_devices = device_settings.Length;
                    for (int i = 0; i < num_of_devices; i++) {
                        if (device_settings[i].changed == true && device_settings[i].active == true) {
                            Console.WriteLine("NMS: " + device_settings[i].name + " settings changed");
                            updateDevice(device_settings[i]);
                            device_settings[i].changed = false;
                            string new_connections = JsonConvert.SerializeObject(device_settings[i].connections, Formatting.Indented);
                        }  
                    }
                } catch {

                } 
                listener.BeginAccept(   
                    new AsyncCallback(AcceptCallback),  
                    listener );  
   
            }  
  
        } catch (Exception e) {  
            Console.WriteLine(e.ToString());  
        }  
  
    }  

    public static void updateConfigFile(DeviceSettings[] ds) {
        string json = JsonConvert.SerializeObject(ds, Formatting.Indented);
        File.WriteAllText(Directory.GetCurrentDirectory() + "/config.json", json);
    }
    public static void setDeviceActiveState(string device_name, bool state) {
        DeviceSettings[] ds = checkConfigFile();
        for (int i=0; i<ds.Length; i++) {
            if (ds[i].name == device_name && ds[i].active == false && state == true) {
                ds[i].active = state;
                Console.WriteLine("NMS: Changed device {0} to active", device_name);
                updateConfigFile(ds);
                return;
            } else if (ds[i].name == device_name && ds[i].active == true && state == false) {
                ds[i].active = state;
                Console.WriteLine("NMS: Changed device {0} to inactive", device_name);
                updateConfigFile(ds);
                return;
            }
        }
    }
    public static void setDeviceToUpdated(string device_name) {
        DeviceSettings[] ds = checkConfigFile();
        for (int i=0; i<ds.Length; i++) {
            if (ds[i].name == device_name) {
                ds[i].changed = false;
                updateConfigFile(ds);
                return;
            }
        }
    }
    public static void updateDevice(DeviceSettings ds) {
        byte[] bytes = new byte[1024]; 
        IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());  
        IPAddress ipAddress = ipHostInfo.AddressList[0];  
        IPEndPoint remoteEP = new IPEndPoint(ipAddress,ds.port);

        Socket sender = new Socket(ipAddress.AddressFamily,   
                SocketType.Stream, ProtocolType.Tcp ); 
    
        try {  
                sender.Connect(remoteEP);  

                byte[] msg = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(ds.connections));  

                int bytesSent = sender.Send(msg);
 
                sender.Shutdown(SocketShutdown.Both);  
                sender.Close();  
                setDeviceToUpdated(ds.name);
                Console.WriteLine("NMS: Device {0} successfully updated", ds.name);

            } catch (ArgumentNullException ane) {  
                Console.WriteLine("NMS: ArgumentNullException : {0}",ane.ToString());  
            } catch (SocketException se) {  
                Console.WriteLine("NMS: Device {0} at {1} is not active", ds.name, remoteEP);  
                setDeviceActiveState(ds.name, false);
            } catch (Exception e) {  
                Console.WriteLine("NMS: Unexpected exception : {0}", e.ToString());  
            } 
    }
    public static DeviceSettings[] checkConfigFile() {
        String dir = Directory.GetCurrentDirectory() + "/config.json";
        String st = File.ReadAllText(dir);
        DeviceSettings[] ds = JsonConvert.DeserializeObject<DeviceSettings[]>(st);
        return ds;
    }
    public static int getNMSport() {
        DeviceSettings[] ds = checkConfigFile();
        for (int i=0; i<ds.Length; i++) 
            if (ds[i].name == "NMS")
                return ds[i].port;
        return 11000;
    }
    public static void AcceptCallback(IAsyncResult ar) {  
        // Signal the main thread to continue.  
        allDone.Set();  
  
        // Get the socket that handles the client request.  
        Socket listener = (Socket) ar.AsyncState;  
        Socket handler = listener.EndAccept(ar);  
  
        // Create the state object.  
        StateObject state = new StateObject();  
        state.workSocket = handler;  
        handler.BeginReceive( state.buffer, 0, StateObject.BufferSize, 0,  
            new AsyncCallback(ReadCallback), state);  
    }  
  
    public static void ReadCallback(IAsyncResult ar) {  
        String content = String.Empty;  
   
        StateObject state = (StateObject) ar.AsyncState;  
        Socket handler = state.workSocket;  
   
        int bytesRead = handler.EndReceive(ar);  
  
        if (bytesRead > 0) {  
            state.sb.Append(Encoding.ASCII.GetString(  
                state.buffer, 0, bytesRead));  
 
            content = state.sb.ToString();  
            if (content.IndexOf("<keep_alive>") > -1) {  
                content = content.Replace("<keep_alive>", "");
                KeepAliveMessage message = JsonConvert.DeserializeObject<KeepAliveMessage>(content);
                Console.WriteLine("NMS: Keep Alive - Device {0} Endpoint {1}", message.name, message.endpoint);
                setDeviceActiveState(message.name, true);
                string response = "Keep alive message received<EOF>"; 
                Send(handler, response);  
            } else {  
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,  
                new AsyncCallback(ReadCallback), state);  
            }  
        }  
    }  
  
    private static void Send(Socket handler, String data) {  

        byte[] byteData = Encoding.ASCII.GetBytes(data);  
        handler.BeginSend(byteData, 0, byteData.Length, 0,  
            new AsyncCallback(SendCallback), handler);  
    }  
    private static void SendCallback(IAsyncResult ar) {  
        try {  
            Socket handler = (Socket) ar.AsyncState;  
   
            int bytesSent = handler.EndSend(ar);  
  
            handler.Shutdown(SocketShutdown.Both);  
            handler.Close();  
  
        } catch (Exception e) {  
            Console.WriteLine("NMS: " + e.ToString());  
        }  
    }  
  
    public static int Main(String[] args) {  

        StartListeningAsync();  
        return 0;  
    }  
}