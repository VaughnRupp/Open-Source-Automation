﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceProcess;
using System.Threading;
using System.Timers;
using System.Xml.Linq;
using MySql.Data.MySqlClient;
using System.Security;
using System.Security.Policy;

namespace OSAE.Service
{
    class OSAEService : ServiceBase
    {
        private ServiceHost sHost;
        private WCF.WCFService wcfService;
        private List<Plugin> plugins = new List<Plugin>();
        private List<Plugin> masterPlugins = new List<Plugin>();
        private string _computerIP;
        private bool goodConnection = false;
        private WebServiceHost serviceHost = new WebServiceHost(typeof(OSAERest.api));
        private OSAE osae = new OSAE("OSAE Service");
        private bool running = true;
        
        System.Timers.Timer timer = new System.Timers.Timer();
        System.Timers.Timer updates = new System.Timers.Timer();
        System.Timers.Timer checkPlugins = new System.Timers.Timer();

        /// <summary>
        /// The Main Thread: This is where your Service is Run.
        /// </summary>
        static void Main(string[] args) 
        {
            if (args.Length > 0)
            {
                OSAE osacl = new OSAE("OSACL");
                string pattern = osacl.MatchPattern(args[0]);
                osacl.AddToLog("Processing command: " + args[0] + ", Named Script: " + pattern, true);
                if (pattern != "")
                    osacl.MethodQueueAdd("Script Processor", "NAMED SCRIPT", pattern, "");
            }
            else
            {
                ServiceBase.Run(new OSAEService());
            }
            
        }
        
        /// <summary>
        /// Public Constructor for WindowsService.
        /// - Put all of your Initialization code here.
        /// </summary>
        public OSAEService()
        {
            osae.AddToLog("Service Starting", true);

            try
            {
                if (!EventLog.SourceExists("OSAE"))
                    EventLog.CreateEventSource("OSAE", "Application");
            }
            catch(Exception ex)
            {
                osae.AddToLog("CreateEventSource error: " + ex.Message, true);
            }
            this.ServiceName = "OSAE";
            this.EventLog.Source = "OSAE";
            this.EventLog.Log = "Application";

            // These Flags set whether or not to handle that specific
            //  type of event. Set to true if you need it, false otherwise.
            
            this.CanStop = true;
            this.CanShutdown = true;
        }

        #region Service Start/Stop Processing
        /// <summary>
        /// OnStart: Put startup code here
        ///  - Start threads, get inital data, etc.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
//#if (DEBUG)
//            Debugger.Launch(); //<-- Simple form to debug a web services 
//#endif

            try
            {
                osae.APIpath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName);
                IPHostEntry ipEntry = Dns.GetHostByName(osae.ComputerName);
                IPAddress[] addr = ipEntry.AddressList;
                _computerIP = addr[0].ToString();

                System.IO.FileInfo file = new System.IO.FileInfo(osae.APIpath + "/Logs/");
                file.Directory.Create();
                if (osae.GetObjectPropertyValue("SYSTEM", "Prune Logs").Value == "TRUE")
                {
                    string[] files = Directory.GetFiles(osae.APIpath + "/Logs/");
                    foreach (string f in files)
                        File.Delete(f);
                }
                string[] stores = Directory.GetFiles(osae.APIpath, "*.store", SearchOption.AllDirectories);
                foreach (string f in stores)
                    File.Delete(f);
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error getting registry settings and/or deleting logs: " + ex.Message, true);
            }

            osae.AddToLog("OnStart", true);
            osae.AddToLog("Removing orphaned methods", true);

            try
            {
                MySqlConnection connection = new MySqlConnection("SERVER=" + osae.DBConnection + ";" +
                    "DATABASE=" + osae.DBName + ";" +
                    "PORT=" + osae.DBPort + ";" +
                    "UID=" + osae.DBUsername + ";" +
                    "PASSWORD=" + osae.DBPassword + ";");
                connection.Open();
                MySqlCommand command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = "SET sql_safe_updates=0; DELETE FROM osae_method_queue;";
                osae.RunQuery(command);
                connection.Close();
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error clearing method queue", true);
            }

            osae.AddToLog("Creating Computer object", true);
            if (osae.GetObjectByName(osae.ComputerName) == null)
            {
                OSAEObject obj = osae.GetObjectByAddress(_computerIP);
                if (obj == null)
                {
                    osae.ObjectAdd(osae.ComputerName, osae.ComputerName, "COMPUTER", _computerIP, "", true);
                    osae.ObjectPropertySet(osae.ComputerName, "Host Name", osae.ComputerName);
                }
                else if (obj.Type == "COMPUTER")
                {
                    osae.ObjectUpdate(obj.Name, osae.ComputerName, obj.Description, "COMPUTER", _computerIP, obj.Container, obj.Enabled);
                    osae.ObjectPropertySet(osae.ComputerName, "Host Name", osae.ComputerName);
                }
                else
                {
                    osae.ObjectAdd(osae.ComputerName + "." + _computerIP, osae.ComputerName, "COMPUTER", _computerIP, "", true);
                    osae.ObjectPropertySet(osae.ComputerName + "." + _computerIP, "Host Name", osae.ComputerName);
                }
            }
            else
            {
                OSAEObject obj = osae.GetObjectByName(osae.ComputerName);
                osae.ObjectUpdate(obj.Name, obj.Name, obj.Description, "COMPUTER", _computerIP, obj.Container, obj.Enabled);
                osae.ObjectPropertySet(obj.Name, "Host Name", osae.ComputerName);
            }

            try
            {
                osae.AddToLog("Creating Service object", true);
                OSAEObject svcobj = osae.GetObjectByName("SERVICE-" + osae.ComputerName);
                if (svcobj == null)
                    osae.ObjectAdd("SERVICE-" + osae.ComputerName, "SERVICE-" + osae.ComputerName, "SERVICE", "", "SYSTEM", true);
                osae.ObjectStateSet("SERVICE-" + osae.ComputerName, "ON");
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error creating service object - " + ex.Message, true);
            }

            try
            {
                serviceHost.Open();
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error starting RESTful web service: " + ex.Message, true);
            }
            
            wcfService = new WCF.WCFService();
            sHost = new ServiceHost(wcfService);
            wcfService.MessageReceived += new EventHandler<WCF.CustomEventArgs>(wcfService_MessageReceived);
            try
            {
                sHost.Open();
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error starting WCF service: " + ex.Message, true);
            }

            Thread QueryCommandQueueThread = new Thread(new ThreadStart(QueryCommandQueue));
            QueryCommandQueueThread.Start(); 

            updates.Interval = 86400000;
            updates.Enabled = true;
            updates.Elapsed += new ElapsedEventHandler(getPluginUpdates_tick);

            Thread loadPluginsThread = new Thread(new ThreadStart(LoadPlugins));
            loadPluginsThread.Start();

            checkPlugins.Interval = 60000;
            checkPlugins.Enabled = true;
            checkPlugins.Elapsed += new ElapsedEventHandler(checkPlugins_tick);

            Thread updateThread = new Thread(() => getPluginUpdates());
            updateThread.Start();
        }

        /// <summary>
        /// OnStop: Put your stop code here
        /// - Stop threads, set final data, etc.
        /// </summary>
        protected override void OnStop()
        {
            osae.AddToLog("stopping...", true);
            try
            {
                checkPlugins.Enabled = false;
                running = false;
                if (sHost.State == CommunicationState.Opened)
                    sHost.Close();
                serviceHost.Close();
                osae.AddToLog("shutting down plugins", true);
                foreach (Plugin p in plugins)
                {
                    if (p.Enabled)
                    {
                        p.Shutdown();
                    }
                }
            }
            catch { }
        }

        protected override void OnShutdown() 
        {
            osae.AddToLog("stopping...", true);
            try
            {
                running = false;
                if (sHost.State == CommunicationState.Opened)
                    sHost.Close();
                serviceHost.Close();
                osae.AddToLog("shutting down plugins", true);
                foreach (Plugin p in plugins)
                {
                    if (p.Enabled)
                    {
                        p.Shutdown();
                    }
                }
            }
            catch { }
        }
        #endregion

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void QueryCommandQueue()
        {
            //timer.Enabled = false;
            while (running)
            {
                try
                {
                    DataSet dataset = new DataSet();
                    MySqlCommand command = new MySqlCommand();
                    command.CommandText = "SELECT method_queue_id, object_name, address, method_name, parameter_1, parameter_2, object_owner FROM osae_v_method_queue ORDER BY entry_time";
                    dataset = osae.RunQuery(command);

                    foreach (DataRow row in dataset.Tables[0].Rows)
                    {

                        OSAEMethod method = new OSAEMethod(row["method_name"].ToString(), row["object_name"].ToString(), row["parameter_1"].ToString(), row["parameter_2"].ToString(), row["address"].ToString(), row["object_owner"].ToString());


                        sendMessageToClients("log", "found method in queue: " + method.ObjectName +
                            "(" + method.MethodName + ")   p1: " + method.Parameter1 +
                            "  p2: " + method.Parameter2);
                        osae.AddToLog("Found method in queue: " + method.MethodName, false);
                        osae.AddToLog("-- object name: " + method.ObjectName, false);
                        osae.AddToLog("-- param 1: " + method.Parameter1, false);
                        osae.AddToLog("-- param 2: " + method.Parameter2, false);
                        osae.AddToLog("-- object owner: " + method.Owner, false);

                        if (method.ObjectName == "SERVICE-" + osae.ComputerName)
                        {
                            if (method.MethodName == "EXECUTE")
                            {
                                sendMessageToClients("command", method.Parameter1
                                    + " | " + method.Parameter2 + " | " + osae.ComputerName);
                            }
                            else if (method.MethodName == "START PLUGIN")
                            {
                                foreach (Plugin p in plugins)
                                {
                                    if (p.PluginName == method.Parameter1)
                                    {
                                        OSAEObject obj = osae.GetObjectByName(p.PluginName);
                                        if (obj != null)
                                        {
                                            enablePlugin(p);
                                        }
                                    }
                                }
                            }
                            else if (method.MethodName == "STOP PLUGIN")
                            {
                                foreach (Plugin p in plugins)
                                {
                                    if (p.PluginName == method.Parameter1)
                                    {
                                        OSAEObject obj = osae.GetObjectByName(p.PluginName);
                                        if (obj != null)
                                        {
                                            disablePlugin(p);
                                        }
                                    }
                                }
                            }
                            else if (method.MethodName == "LOAD PLUGIN")
                            {
                                LoadPlugins();
                            }
                            command.CommandText = "DELETE FROM osae_method_queue WHERE method_queue_id=" + row["method_queue_id"].ToString();
                            osae.AddToLog("Removing method from queue: " + command.CommandText, false);
                            osae.RunQuery(command);
                        }
                        else
                        {
                            bool processed = false;
                            foreach (Plugin plugin in plugins)
                            {
                                if (plugin.Enabled == true && (method.Owner.ToLower() == plugin.PluginName.ToLower() || method.ObjectName.ToLower() == plugin.PluginName.ToLower()))
                                {
                                    command.CommandText = "DELETE FROM osae_method_queue WHERE method_queue_id=" + row["method_queue_id"].ToString();
                                    osae.AddToLog("Removing method from queue: " + command.CommandText, false);
                                    osae.RunQuery(command);
                                   
                                    plugin.ExecuteCommand(method);
                                    processed = true;
                                    break;
                                }
                            }

                            if (!processed)
                            {
                                sendMessageToClients("method", method.ObjectName + " | " + method.Owner + " | "
                                    + method.MethodName + " | " + method.Parameter1 + " | " + method.Parameter2 + " | " 
                                    + method.Address + " | " + row["method_queue_id"].ToString());

                                
                                command.CommandText = "DELETE FROM osae_method_queue WHERE method_queue_id=" + row["method_queue_id"].ToString();
                                osae.AddToLog("Removing method from queue: " + command.CommandText, false);
                                osae.RunQuery(command);
                                processed = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    osae.AddToLog("Error in QueryCommandQueue: " + ex.Message, true);
                    //timer.Enabled = true;
                }
                System.Threading.Thread.Sleep(100);
            }
            //timer.Enabled = true;
        }

        /// <summary>
        /// 
        /// </summary>
        public void LoadPlugins()
        {
            var pluginAssemblies = new List<OSAEPluginBase>();
            var types = PluginFinder.FindPlugins();

            osae.AddToLog("Loading Plugins", true);

            foreach (var type in types)
            {
                osae.AddToLog("type.TypeName: " + type.TypeName, false);
                osae.AddToLog("type.AssemblyName: " + type.AssemblyName, false);

                var domain = CreateSandboxDomain("Sandbox Domain", type.Location, SecurityZone.Internet);

                plugins.Add(new Plugin(type.AssemblyName, type.TypeName, domain, type.Location));
            }

            osae.AddToLog("Found " + plugins.Count.ToString() + " plugins", true);
            MySqlConnection connection = new MySqlConnection("SERVER=" + osae.DBConnection + ";" +
                            "DATABASE=" + osae.DBName + ";" +
                            "PORT=" + osae.DBPort + ";" +
                            "UID=" + osae.DBUsername + ";" +
                            "PASSWORD=" + osae.DBPassword + ";");

            foreach (Plugin plugin in plugins)
            {
                try
                {
                    osae.AddToLog("---------------------------------------", true);
                    osae.AddToLog("Plugin name: " + plugin.PluginName, true);
                    osae.AddToLog("Testing connection", true);
                    if (!goodConnection)
                    {
                        try
                        {
                            connection.Open();
                            goodConnection = true;
                        }
                        catch
                        {
                        }
                    }

                    if (goodConnection)
                    {
                        if (plugin.PluginName != "")
                        {
                            OSAEObject obj = osae.GetObjectByName(plugin.PluginName);
                            if (obj != null)
                            {
                                osae.AddToLog("Plugin Object found: " + obj.Name + " - Enabled: " + obj.Enabled.ToString(), true);
                                if (obj.Enabled == 1)
                                {
                                    enablePlugin(plugin);
                                }
                                else
                                    plugin.Enabled = false;

                                osae.AddToLog("Status: " + plugin.Enabled.ToString(), true);
                                osae.AddToLog("PluginVersion: " + plugin.PluginVersion, true);
                            }
                        }
                        else
                        {
                            //add code to create the object.  We need the plugin to specify the type though

                            MySqlDataAdapter adapter;
                            DataSet dataset = new DataSet();
                            MySqlCommand command = new MySqlCommand();
                            command.Connection = connection;
                            command.CommandText = "SELECT * FROM osae_object_type_property p inner join osae_object_type t on p.object_type_id = t.object_type_id WHERE object_type=@type AND property_name='Computer Name'";
                            command.Parameters.AddWithValue("@type", plugin.PluginType);
                            adapter = new MySqlDataAdapter(command);
                            adapter.Fill(dataset);

                            if (dataset.Tables[0].Rows.Count > 0)
                                plugin.PluginName = plugin.PluginType + "-" + osae.ComputerName;
                            else
                                plugin.PluginName = plugin.PluginType;
                            osae.AddToLog("Plugin object does not exist in DB: " + plugin.PluginName, true);
                            osae.ObjectAdd(plugin.PluginName, plugin.PluginName, plugin.PluginType, "", "System", false);
                            osae.ObjectPropertySet(plugin.PluginName, "Computer Name", osae.ComputerName);

                            osae.AddToLog("Plugin added to DB: " + plugin.PluginName, true);
                            sendMessageToClients("plugin", plugin.PluginName + " | " + plugin.Enabled.ToString() + " | " + plugin.PluginVersion + " | Stopped | " + plugin.LatestAvailableVersion + " | " + plugin.PluginType + " | " + osae.ComputerName);

                        }
                        masterPlugins.Add(plugin);
                    }


                }
                catch (Exception ex)
                {
                    osae.AddToLog("Error loading plugin: " + ex.Message, true);
                }
                catch
                {
                    osae.AddToLog("Error loading plugin", true);
                }
            }

        }

        #region WCF Events and Methods

        /// <summary>
        /// Event happens when a wcf client invokes it
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void wcfService_MessageReceived(object source, WCF.CustomEventArgs e)
        {
            try
            {
                osae.AddToLog("received message: " + e.Message, false);
                if (e.Message == "connected")
                {
                    try
                    {
                        osae.AddToLog("client connected", false);
                        foreach (Plugin p in masterPlugins)
                        {
                            string msg = p.PluginName + " | " + p.Enabled.ToString() + " | " + p.PluginVersion + " | " + p.Status + " | " + p.LatestAvailableVersion + " | " + p.PluginType + " | " + osae.ComputerName;

                            sendMessageToClients("plugin", msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        osae.AddToLog("Error sending plugin messages to clients: " + ex.Message, true);
                    }
                }
                else
                {
                    string[] arguments = e.Message.Split('|');
                    if (arguments[0] == "ENABLEPLUGIN")
                    {
                        bool local = false;
                        if (arguments[2] == "True")
                            osae.ObjectStateSet(arguments[1], "ON");
                        else if (arguments[2] == "False")
                            osae.ObjectStateSet(arguments[1], "OFF");
                        foreach (Plugin p in plugins)
                        {
                            if (p.PluginName == arguments[1])
                            {
                                local = true;
                                OSAEObject obj = osae.GetObjectByName(p.PluginName);
                                if (obj != null)
                                {
                                    if (arguments[2] == "True")
                                    {                                        
                                        enablePlugin(p);
                                    }
                                    else if (arguments[2] == "False")
                                    {
                                        disablePlugin(p);
                                    }
                                }
                            }
                        }
                        if (!local)
                        {
                            sendMessageToClients("enablePlugin", e.Message);
                        }
                    }
                    else if (arguments[0] == "plugin")
                    {
                        bool found = false;
                        foreach (Plugin plugin in masterPlugins)
                        {

                            if (plugin.PluginName == arguments[1])
                            {
                                if (arguments[4].ToLower() == "true")
                                    plugin.Enabled = true;
                                else
                                    plugin.Enabled = false;
                                plugin.PluginVersion = arguments[3];
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            Plugin p = new Plugin();
                            p.PluginName = arguments[1];
                            p.PluginVersion = arguments[3];
                            if (arguments[4].ToLower() == "true")
                                p.Enabled = true;
                            else
                                p.Enabled = false;
                            masterPlugins.Add(p);
                        }
                    }
                    else if (arguments[0] == "updatePlugin")
                    {
                        foreach (Plugin plugin in masterPlugins)
                        {
                            if (plugin.PluginName == arguments[1])
                            {
                                if (plugin.Status == "Running")
                                    disablePlugin(plugin);

                                //code for downloading and installing plugin
                                break;
                            }
                        }
                    }
                }

                osae.AddToLog("-----------Master plugin list", false);
                foreach (Plugin p in masterPlugins)
                    osae.AddToLog(" --- " + p.PluginName, false);
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error receiving message: " + ex.Message, true);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="msgType"></param>
        /// <param name="message"></param>
        private void sendMessageToClients(string msgType, string message)
        {
            try
            {
                osae.AddToLog("Sending message to clients: " + msgType + " - " + message, false);
                Thread thread = new Thread(() => wcfService.SendMessageToClients(msgType, message, osae.ComputerName));
                thread.Start();
            }
            catch(Exception ex)
            {
                osae.AddToLog("Error sending message to clients: " + ex.Message, true);
            }
        }

        #endregion

        #region Check For Plugin Updates

        private void getPluginUpdates_tick(object source, EventArgs e)
        {
            //getPluginUpdates();
            Thread usageThread = new Thread(() => getPluginUpdates());
            usageThread.Start();
        }

        private void checkForUpdates(string name, string version)
        {
            try
            {
                Plugin p = new Plugin();
                bool plug = false;
                foreach (Plugin plugin in plugins)
                {
                    if (plugin.PluginType == name)
                    {
                        p = plugin;
                        plug = true;
                    }
                }
                int curMajor, curMinor, curRevion, latestMajor = 0, latestMinor = 0, latestRevision = 0;
                string[] split = version.Split('.');
                curMajor = Int32.Parse(split[0]);
                curMinor = Int32.Parse(split[1]);
                curRevion = Int32.Parse(split[2]);

                string url = "http://www.opensourceautomation.com/pluginUpdates.php?app=" + name + "&ver=" + version;
                osae.AddToLog("Checking for plugin updates: " + url, false);
                WebRequest request = HttpWebRequest.Create(url);
                WebResponse response = request.GetResponse();
                Stream responseStream = response.GetResponseStream();
                //XmlReader rdr = XmlReader.Create(responseStream);
                
                XElement xml  = XElement.Load(responseStream);
                osae.AddToLog("XML retreived", false);

                var query = from e in xml.Elements("plugin")
                            select new { B = e.Element("major").Value, C = e.Element("minor").Value, D = e.Element("revision").Value };

                foreach (var e in query)
                {
                    latestMajor = Int32.Parse(e.B);
                    latestMinor = Int32.Parse(e.C);
                    latestRevision = Int32.Parse(e.D);
                }
                
                
                if (latestMajor >= curMajor)
                {
                    if (latestMinor >= curMinor)
                    {
                        if (latestRevision > curRevion)
                        {
                            p.LatestAvailableVersion = latestMajor + "." + latestMinor + "." + latestRevision;
                            osae.AddToLog("current version: " + curMajor + "." + curMinor + "." + curRevion, false);
                            osae.AddToLog("latest version: " + p.LatestAvailableVersion, false);
                            string msg;

                            if (!plug)
                            {
                                msg = version + "|" + latestMajor + "." + latestMinor + "." + latestRevision;
                                sendMessageToClients("service", msg);
                            }
                        }
                    }
                }
                
                response.Close();

                if (plug)
                {
                    string msg = p.PluginName + " | " + p.Enabled.ToString() + " | " + p.PluginVersion + " | " + p.Status + " | " + p.LatestAvailableVersion + " | " + p.PluginType + " | " + osae.ComputerName;
                    sendMessageToClients("plugin", msg);
                }
            }
            catch (Exception ex)
            {
                osae.AddToLog("plugin update error: " + ex.Message, true);
            }
        }

        private void getPluginUpdates()
        {
            foreach (Plugin plugin in plugins)
            {
                osae.AddToLog("Checking for update: " + plugin.PluginName, false);
                try
                {
                    //Thread usageThread = new Thread(() => checkForUpdates(plugin.PluginType, plugin.PluginVersion));
                    //usageThread.Start();
                    checkForUpdates(plugin.PluginType, plugin.PluginVersion);
                }
                catch { }
            }
            try
            {
                checkForUpdates("Service", osae.GetObjectPropertyValue("SYSTEM", "DB Version").Value);
            }
            catch { }
            
        }

        #endregion

        #region Monitor Plugins

        private void checkPlugins_tick(object source, EventArgs e)
        {
            //foreach (Plugin plugin in plugins)
            //{
            //    try
            //    {
            //        if (plugin.Enabled)
            //        {
            //            Process process = Process.GetProcessById(plugin.process.ProcessId);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        osae.AddToLog(plugin.PluginName + " - Plugin has crashed. Attempting to restart.", true);
            //        enablePlugin(plugin);
            //        osae.AddToLog("New Process ID: " + plugin.process.ProcessId, true);
            //    }
            
            //}

        }

        #endregion

        #region Helper functions
        public string TrimNulls(byte[] data)
        {
            int rOffset = data.Length - 1;

            for (int i = data.Length - 1; i >= 0; i--)
            {
                rOffset = i;

                if (data[i] != (byte)0) break;
            }

            return System.Text.Encoding.ASCII.GetString(data, 0, rOffset + 1);
        }

        public bool pluginExist(string name)
        {
            foreach (Plugin p in plugins)
            {
                if (p.PluginType == name)
                    return true;
            }
            return false;
        }

        public void enablePlugin(Plugin plugin)
        {
            OSAEObject obj = osae.GetObjectByName(plugin.PluginName);
            osae.ObjectUpdate(plugin.PluginName, plugin.PluginName, obj.Description, obj.Type, obj.Address, obj.Container, 1);
            try
            {
                if (plugin.ActivatePlugin())
                {
                    plugin.Enabled = true;
                    plugin.RunInterface();
                    osae.ObjectStateSet(plugin.PluginName, "ON");
                    sendMessageToClients("plugin", plugin.PluginName + " | " + plugin.Enabled.ToString() + " | " + plugin.PluginVersion + " | Running | " + plugin.LatestAvailableVersion + " | " + plugin.PluginType + " | " + osae.ComputerName);
                    osae.AddToLog("Plugin enabled: " + plugin.PluginName, true);
                }
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error activating plugin (" + plugin.PluginName + "): " + ex.Message + " - " + ex.InnerException, true);
            }
            catch
            {
                osae.AddToLog("Error activating plugin", true);
            }
        }

        public void disablePlugin(Plugin p)
        {
            osae.AddToLog("Disabling Plugin: " + p.PluginName,true);
            OSAEObject obj = osae.GetObjectByName(p.PluginName);
            osae.ObjectUpdate(p.PluginName, p.PluginName, obj.Description, obj.Type, obj.Address, obj.Container, 0);
            try
            {
                p.Shutdown();
                p.Enabled = false;
                p.Domain = CreateSandboxDomain("Sandbox Domain", p.Location, SecurityZone.Internet);
                sendMessageToClients("plugin", p.PluginName + " | " + p.Enabled.ToString() + " | " + p.PluginVersion + " | Stopped | " + p.LatestAvailableVersion + " | " + p.PluginType + " | " + osae.ComputerName);
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error stopping plugin (" + p.PluginName + "): " + ex.Message + " - " + ex.InnerException, true);
            }
        }

        public AppDomain CreateSandboxDomain(string name, string path, SecurityZone zone)
        {
            var setup = new AppDomainSetup { ApplicationBase = osae.APIpath, PrivateBinPath = Path.GetFullPath(path) };

            var evidence = new Evidence();
            evidence.AddHostEvidence(new Zone(zone));
            var permissions = SecurityManager.GetStandardSandbox(evidence);

            var strongName = typeof(OSAEService).Assembly.Evidence.GetHostEvidence<StrongName>();

            return AppDomain.CreateDomain(name, null, setup);
        }

        #endregion

    }

}
