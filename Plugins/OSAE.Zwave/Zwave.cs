﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using OpenZWaveDotNet;
using System.Threading;
using System.AddIn;
using OpenSourceAutomation;

namespace OSAE.Zwave
{
    [AddIn("ZWave", Version = "0.3.23")]
    public class Zwave : IOpenSourceAutomationAddInv2
    {
        static private OSAE osae = new OSAE("ZWave");
        static private ManagedControllerStateChangedHandler m_controllerStateChangedHandler = new ManagedControllerStateChangedHandler(Zwave.MyControllerStateChangedHandler);
        static private ZWManager m_manager = null;
        ZWOptions m_options = null;
        UInt32 m_homeId = 0;
        ZWNotification m_notification = null;
        List<Node> m_nodeList = new List<Node>();
        string pName;

        public void RunInterface(string pluginName)
        {
            pName = pluginName;
            int poll = 60;
            if (osae.GetObjectPropertyValue(pName, "Polling Interval").Value != "")
                poll = Int32.Parse(osae.GetObjectPropertyValue(pName, "Polling Interval").Value);

            string port = osae.GetObjectPropertyValue(pName, "Port").Value;

            osae.AddToLog("Port: " + port, true);
            try
            {
                if (port != "")
                {
                    // Create the Options
                    m_options = new ZWOptions();
                    m_options.Create(osae.APIpath + @"\AddIns\ZWave\config\", osae.APIpath + @"\AddIns\ZWave\", @"");

                    // Add any app specific options here...
                    m_options.AddOptionBool("ConsoleOutput", false);
                    m_options.AddOptionBool("IntervalBetweenPolls", true);
                    m_options.AddOptionInt("PollInterval", poll);


                    // Lock the options
                    m_options.Lock();

                    // Create the OpenZWave Manager
                    m_manager = new ZWManager();
                    m_manager.Create();
                    m_manager.OnNotification += new ManagedNotificationsHandler(NotificationHandler);

                    // Add a driver
                    m_manager.AddDriver(@"\\.\COM" + port);

                    //osae.AddToLog("Setting poll interval: " + poll.ToString(), true);
                    //m_manager.SetPollInterval(poll);
                    osae.AddToLog(osae.APIpath + @"\AddIns\ZWave\Config", true);
                    osae.AddToLog("Zwave plugin initialized", true);
                }

                osae.ObjectTypeUpdate("ZWAVE DIMMER", "ZWAVE DIMMER", "ZWave Dimmer", pName, "MULTILEVEL SWITCH", 0, 0, 0, 1);
                osae.ObjectTypeUpdate("ZWAVE BINARY SWITCH", "ZWAVE BINARY SWITCH", "ZWave Binary Switch", pName, "BINARY SWITCH", 0, 0, 0, 1);
                osae.ObjectTypeUpdate("ZWAVE THERMOSTAT", "ZWAVE THERMOSTAT", "ZWave Thermostat", pName, "THERMOSTAT", 0, 0, 0, 1);
                osae.ObjectTypeUpdate("ZWAVE REMOTE", "ZWAVE REMOTE", "ZWave Remote", pName, "ZWAVE REMOTE", 0, 0, 0, 1);
                osae.ObjectTypeUpdate("ZWAVE MULTISENSOR", "ZWAVE MULTISENSOR", "ZWave MultiSensor", pName, "ZWAVE MULTISENSOR", 0, 0, 0, 1);
                osae.ObjectTypeUpdate("ZWAVE HOME ENERGY METER", "ZWAVE HOME ENERGY METER", "ZWave Home Energy Meter", pName, "ZWAVE HOME ENERGY METER", 0, 0, 0, 1);
                osae.ObjectTypeUpdate("ZWAVE SMART ENERGY SWITCH", "ZWAVE SMART ENERGY SWITCH", "ZWave Smart Energy Switch", pName, "ZWAVE SMART ENERGY SWITCH", 0, 0, 0, 1);

                #region Screen Init
                //osae.ObjectPropertySet("Screen - ZWave Manager - ZWave - ADD CONTROLLER", "Object Name", pName);
                //osae.ObjectPropertySet("Screen - ZWave Manager - ZWave - ADD DEVICE", "Object Name", pName);
                //osae.ObjectPropertySet("Screen - ZWave Manager - ZWave - REMOVE CONTROLLER", "Object Name", pName);
                //osae.ObjectPropertySet("Screen - ZWave Manager - ZWave - REMOVE DEVICE", "Object Name", pName);
                #endregion
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error initalizing plugin: " + ex.Message, true);
            }
        }

        public void ProcessCommand(OSAEMethod method)
        {
            osae.AddToLog("Found Command: " + method.MethodName + " | param1: " + method.Parameter1 + " | param2: " + method.Parameter2 + " | obj: " + method.ObjectName, false);
            //process command
            try
            {
                if (method.Address.Length > 0)
                {
                    int address;
                    byte instance = 0;
                    byte nid;
                    if (int.TryParse(method.Address.Substring(1), out address))
                    {
                        nid = (byte)address;
                    }
                    else
                    {
                        nid = (byte)Int32.Parse(method.Address.Substring(1).Split('-')[0]);
                        instance = (byte)Int32.Parse(method.Address.Substring(1).Split('-')[1]);
                    }
                    Node node = GetNode(m_homeId, nid);

                    if (method.MethodName == "NODE NEIGHBOR UPDATE")
                    {
                        osae.AddToLog("Requesting Node Neighbor Update: " + osae.GetObjectByAddress("Z" + nid.ToString()).Name, true);
                        m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                        if (!m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.RequestNodeNeighborUpdate, false, nid))
                        {
                            osae.AddToLog("Request Node Neighbor Update Failed: " + osae.GetObjectByAddress("Z" + nid.ToString()).Name, true);
                            m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                        }
                    }
                    else if (method.MethodName == "ENABLE POLLING")
                        enablePolling(nid);
                    else
                    {
                        switch (node.Label)
                        {
                            #region Binary Switch
                            case "Binary Switch":
                            case "Binary Power Switch":
                                if (method.MethodName == "ON")
                                {
                                    if (instance != 0)
                                    {
                                        foreach (Value value in node.Values)
                                        {
                                            if (value.Label == "Switch" && value.ValueID.GetInstance() == instance)
                                                m_manager.SetValue(value.ValueID, true);
                                        }

                                    }
                                    else
                                        m_manager.SetNodeOn(m_homeId, nid);
                                    osae.ObjectStateSet(method.ObjectName, "ON");
                                    osae.AddToLog("Turned light on: " + method.ObjectName, false);
                                }
                                else
                                {
                                    if (instance != 0)
                                    {
                                        foreach (Value value in node.Values)
                                        {
                                            if (value.Label == "Switch" && value.ValueID.GetInstance() == instance)
                                                m_manager.SetValue(value.ValueID, false);
                                        }

                                    }
                                    else
                                        m_manager.SetNodeOff(m_homeId, nid);
                                    osae.ObjectStateSet(method.ObjectName, "OFF");
                                    osae.AddToLog("Turned light off: " + method.ObjectName, false);
                                }
                                break;
                            #endregion

                            #region Dimmers
                            case "Multilevel Switch":
                            case "Multilevel Power Switch":
                            case "Multilevel Scene Switch":
                                if (method.MethodName == "ON")
                                {
                                    byte lvl;
                                    if (method.Parameter1 != "")
                                        lvl = (byte)Int32.Parse(method.Parameter1);
                                    else if (osae.GetObjectPropertyValue(method.ObjectName, "Default Dim").Value != "")
                                        lvl = (byte)Int32.Parse(osae.GetObjectPropertyValue(method.ObjectName, "Default Dim").Value);
                                    else
                                        lvl = (byte)100;

                                    m_manager.SetNodeLevel(m_homeId, nid, lvl);
                                    osae.ObjectStateSet(method.ObjectName, "ON");
                                    osae.AddToLog("Turned light on: " + method.ObjectName + "|" + method.Parameter1, false);
                                }
                                else
                                {
                                    m_manager.SetNodeOff(m_homeId, nid);
                                    osae.ObjectStateSet(method.ObjectName, "OFF");
                                    osae.AddToLog("Turned light off: " + method.ObjectName, false);
                                }
                                break;
                            #endregion

                            #region Thermostats
                            case "General Thermostat V2":
                                if (method.MethodName == "ON")
                                {
                                    m_manager.SetNodeOn(m_homeId, nid);
                                    osae.ObjectStateSet(method.ObjectName, "ON");
                                    osae.AddToLog("Turned thermostat on: " + method.ObjectName, false);
                                }
                                else if (method.MethodName == "OFF")
                                {
                                    m_manager.SetNodeOff(m_homeId, nid);
                                    osae.ObjectStateSet(method.ObjectName, "OFF");
                                    osae.AddToLog("Turned thermostat off: " + nid.ToString(), false);
                                }
                                else if (method.MethodName == "COOLSP")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Cooling 1")
                                        {
                                            m_manager.SetValue(value.ValueID, Convert.ToSingle(method.Parameter1));
                                            osae.AddToLog("Set cool target temperature to " + method.Parameter1 + ": " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "HEATSP")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Heating 1")
                                        {
                                            m_manager.SetValue(value.ValueID, Convert.ToSingle(method.Parameter1));
                                            osae.AddToLog("Set heat target temperature to " + method.Parameter1 + ": " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "UNIT OFF")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "Off");
                                            osae.AddToLog("Set Unit Mode to Off: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "HEAT")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "Heat");
                                            osae.AddToLog("Set Unit Mode to Heat: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "COOL")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "Cool");
                                            osae.AddToLog("Set Unit Mode to Cool: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "AUTO")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "Auto");
                                            osae.AddToLog("Set Unit Mode to Auto: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "AUX HEAT")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "Aux Heat");
                                            osae.AddToLog("Set Unit Mode to Aux Heat: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "FAN ON")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Fan Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "On Low");
                                            osae.AddToLog("Set Fan Mode to On: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                else if (method.MethodName == "FAN AUTO")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Fan Mode")
                                        {
                                            m_manager.SetValueListSelection(value.ValueID, "Auto Low");
                                            osae.AddToLog("Set Fan Mode to Auto: " + method.ObjectName, false);
                                        }
                                    }
                                }
                                break;
                            #endregion

                            #region MultiSensor
                            case "Binary Routing Sensor":
                                if (method.MethodName == "SET WAKEUP INTERVAL")
                                {
                                    foreach (Value value in node.Values)
                                    {
                                        if (value.Label == "Wake-up Interval")
                                        {
                                            m_manager.SetValue(value.ValueID, Convert.ToSingle(method.Parameter1));
                                            osae.AddToLog("Set wake-up interval to " + method.Parameter1 + ": " + method.ObjectName, false);
                                        }
                                    }
                                }
                                break;
                            #endregion
                        }
                    }
                }
                else
                {
                    #region Controller Commands
                    try
                    {
                        byte nid = 0xff;
                        if (method.Parameter1 != "")
                            nid = (byte)Int32.Parse(method.Parameter1.Substring(1));

                        switch (method.MethodName)
                        {
                            case "ADD CONTROLLER":
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (!m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.AddController, false, nid))
                                {
                                    osae.AddToLog("Add Controller Failed", true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                //osae.MethodQueueAdd(osae.GetPluginName("GUI CLIENT", osae.ComputerName), "POPUP MESSAGE", "Put the target controller into receive configuration mode.\nThe PC Z-Wave Controller must be within 2m of the controller being added.", "");
                                break;
                            case "REMOVE CONTROLLER":
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (!m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.RemoveController, false, nid))
                                {
                                    osae.AddToLog("Remove Controller Failed", true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                //osae.MethodQueueAdd(osae.GetPluginName("GUI CLIENT", osae.ComputerName), "POPUP MESSAGE", "Put the target controller into receive configuration mode.\nThe PC Z-Wave Controller must be within 2m of the controller being removed.", "");
                                break;
                            case "ADD DEVICE":
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (!m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.AddDevice, false, nid))
                                {
                                    osae.AddToLog("Add Device Failed", true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                //osae.MethodQueueAdd(osae.GetPluginName("GUI CLIENT", osae.ComputerName), "POPUP MESSAGE", "Press the program button on the Z-Wave device to add it to the network.\nFor security reasons, the PC Z-Wave Controller must be close to the device being added.", "");
                                break;
                            case "REMOVE DEVICE":
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.RemoveDevice, false, nid))
                                {
                                    osae.ObjectDelete(osae.GetObjectByAddress("Z" + nid.ToString()).Name);
                                }
                                else
                                {
                                    osae.AddToLog("Remove Device Failed", true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                //osae.MethodQueueAdd(osae.GetPluginName("GUI CLIENT", osae.ComputerName), "POPUP MESSAGE", "Press the program button on the Z-Wave device to remove it from the network.\nFor security reasons, the PC Z-Wave Controller must be close to the device being removed.", "");
                                break;
                            case "REMOVE FAILED NODE":
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.RemoveFailedNode, false, nid))
                                {
                                    osae.ObjectDelete(osae.GetObjectByAddress("Z" + nid.ToString()).Name);
                                }
                                else
                                {
                                    osae.AddToLog("Remove Failed Node Failed: Z" + nid.ToString(), true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                break;
                            case "RESET CONTROLLER":
                                osae.AddToLog("Resetting Controller and deleting all ZWave objects", true);
                                m_manager.ResetController(m_homeId);
                                //DataSet ds = osae.GetObjectsByType("ZWAVE DIMMER");
                                //foreach (DataRow dr in ds.Tables[0].Rows)
                                //    osae.ObjectDelete(dr["object_name"].ToString());
                                //ds = osae.GetObjectsByType("ZWAVE BINARY SWITCH");
                                //foreach (DataRow dr in ds.Tables[0].Rows)
                                //    osae.ObjectDelete(dr["object_name"].ToString());
                                //ds = osae.GetObjectsByType("ZWAVE THERMOSTAT");
                                //foreach (DataRow dr in ds.Tables[0].Rows)
                                //    osae.ObjectDelete(dr["object_name"].ToString());
                                break;
                            case "NODE NEIGHBOR UPDATE":
                                osae.AddToLog("Requesting Node Neighbor Update: Z" + nid.ToString(), true);
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (!m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.RequestNodeNeighborUpdate, false, nid))
                                {
                                    osae.AddToLog("Request Node Neighbor Update Failed: Z" + nid.ToString(), true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                break;
                            case "NETWORK UPDATE":
                                osae.AddToLog("Requesting Network Update", true);
                                m_manager.OnControllerStateChanged += m_controllerStateChangedHandler;
                                if (!m_manager.BeginControllerCommand(m_homeId, ZWControllerCommand.RequestNetworkUpdate, false, nid))
                                {
                                    osae.AddToLog("Request Network Update Failed: Z" + nid.ToString(), true);
                                    m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
                                }
                                break;
                            case "ENABLE POLLING":
                                enablePolling(nid);
                                break;
                        }

                    }
                    catch (Exception ex)
                    {
                        osae.AddToLog("Controller command failed (" + method.MethodName + "): " + ex.Message + " -- " + ex.StackTrace
                            + " -- " + ex.InnerException, true);
                    }
                    #endregion
                }

            }
            catch (Exception ex)
            {
                osae.AddToLog("Error Processing Command - " + ex.Message + " -" + ex.InnerException, true);
            }

        }

        public void Shutdown()
        {
            m_manager.RemoveDriver(@"\\.\COM" + osae.GetObjectPropertyValue(pName, "Port").Value);
            m_manager = null;
        }

        public void NotificationHandler(ZWNotification notification)
        {
            m_notification = notification;
            NotificationHandler();
            m_notification = null;
        }

        private void NotificationHandler()
        {
            Node node2 = GetNode(m_notification.GetHomeId(), m_notification.GetNodeId());

            osae.AddToLog("Notification: " + m_notification.GetType().ToString() + " | Node: " + node2.ID.ToString(), true);
            switch (m_notification.GetType())
            {
                #region ValueAdded
                case ZWNotification.Type.ValueAdded:
                    {

                        Node node = GetNode(m_homeId, m_notification.GetNodeId());
                        Value value = new Value();
                        //osae.AddToLog("ValueAdded start: node:" + node.ID.ToString(), true);
                        ZWValueID vid = m_notification.GetValueID();
                        value.ValueID = vid;
                        value.Label = m_manager.GetValueLabel(vid);
                        value.Genre = vid.GetGenre().ToString();
                        value.Index = vid.GetIndex().ToString();
                        value.Type = vid.GetType().ToString();
                        value.CommandClassID = vid.GetCommandClassId().ToString();
                        switch (node.Label)
                        {
                            case "Binary Switch":
                            case "Binary Power Switch":
                                if (value.Genre == "User")
                                {
                                    bool v;
                                    if (m_manager.GetValueAsBool(vid, out v))
                                        value.Val = v.ToString();
                                }
                                break;
                            case "Multilevel Switch":
                            case "Multilevel Power Switch":
                            case "Multilevel Scene Switch":
                                if (value.Genre == "User" && value.Label == "Level")
                                {
                                    byte v;
                                    if (m_manager.GetValueAsByte(vid, out v))
                                        value.Val = v.ToString();
                                }
                                else if (value.Label == "Power")
                                {
                                    decimal v;
                                    if (m_manager.GetValueAsDecimal(vid, out v))
                                        value.Val = v.ToString();
                                }
                                break;
                            case "General Thermostat V2":
                                if (value.Label == "Temperature")
                                {
                                    decimal v;
                                    if (m_manager.GetValueAsDecimal(vid, out v))
                                        value.Val = v.ToString();
                                }
                                break;
                            case "Routing Multilevel Sensor":
                                if (value.Label == "Temperature")
                                {
                                    decimal v;
                                    if (m_manager.GetValueAsDecimal(vid, out v))
                                        value.Val = v.ToString();
                                }
                                else if (value.Label == "Power")
                                {
                                    decimal v;
                                    if (m_manager.GetValueAsDecimal(vid, out v))
                                        value.Val = v.ToString();
                                }
                                break;
                            case "Routing Binary Sensor":
                                if (value.Label == "Sensor")
                                {
                                    bool v;
                                    if (m_manager.GetValueAsBool(vid, out v))
                                        value.Val = v.ToString();
                                }
                                break;

                        }


                        node.AddValue(value);

                        osae.AddToLog("ValueAdded: node:" + node.ID + " | type: " + value.Type
                            + " | genre: " + value.Genre + " | cmdClsID:" + value.CommandClassID
                            + " | index: " + value.Index + " | instance: " + vid.GetInstance().ToString()
                            + " | readOnly: " + m_manager.IsValueReadOnly(value.ValueID).ToString()
                            + " | value: " + value.Val + " | label: " + m_manager.GetValueLabel(vid), true);
                        break;
                    }
                #endregion

                #region ValueRemoved
                case ZWNotification.Type.ValueRemoved:
                    {
                        try
                        {
                            osae.AddToLog("ValueRemoved: ", true);
                            Node node = GetNode(m_homeId, m_notification.GetNodeId());
                            ZWValueID vid = m_notification.GetValueID();
                            Value val = node.GetValue(vid);
                            node.RemoveValue(val);
                        }
                        catch (Exception ex)
                        {
                            osae.AddToLog("ValueRemoved error: " + ex.Message, true);
                        }
                        break;
                    }
                #endregion

                #region ValueChanged
                case ZWNotification.Type.ValueChanged:
                    {
                        try
                        {
                            Node node = GetNode(m_homeId, m_notification.GetNodeId());
                            osae.AddToLog("ValueChanged start: node:" + node.ID.ToString(), false);
                            ZWValueID vid = m_notification.GetValueID();
                            Value value = node.GetValue(vid);
                            osae.AddToLog("value:" + value.Val, false);
                            OSAEObject nodeObject = osae.GetObjectByAddress("Z" + m_notification.GetNodeId());
                            string v;
                            m_manager.GetValueAsString(vid, out v);
                            value.Val = v;
                            switch (node.Label)
                            {
                                case "Binary Switch":
                                case "Binary Power Switch":
                                    if (value.Label == "Switch")
                                    {
                                        if (value.Val == "True")
                                            osae.ObjectStateSet(nodeObject.Name, "ON");
                                        else
                                            osae.ObjectStateSet(nodeObject.Name, "OFF");
                                    }
                                    break;
                                case "Multilevel Switch":
                                case "Multilevel Power Switch":
                                case "Multilevel Scene Switch":
                                    if (value.Label == "Level" || value.Label == "Basic")
                                    {
                                        node.Level = value.Val;
                                        if (Int32.Parse(value.Val) > 0)
                                            osae.ObjectStateSet(nodeObject.Name, "ON");
                                        else
                                            osae.ObjectStateSet(nodeObject.Name, "OFF");
                                        osae.ObjectPropertySet(nodeObject.Name, "Level", node.Level);
                                    }
                                    else if (value.Label == "Power")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Power", node.Level);
                                    }
                                    break;
                                case "General Thermostat V2":
                                    if (value.Label == "Temperature")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Temperature", node.Level);
                                    }
                                    else if (value.Label == "Operating State")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Operating Mode", node.Level);
                                    }
                                    else if (value.Label == "Fan State")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Fan State", node.Level);
                                    }
                                    else if (value.Label == "Fan Mode")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Fan Mode", node.Level);
                                    }
                                    else if (value.Label == "Heating 1")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Heat Setpoint", node.Level);
                                    }
                                    else if (value.Label == "Cooling 1")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Cool Setpoint", node.Level);
                                    }
                                    else if (value.Label == "Battery Level")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Battery Level", node.Level);
                                    }
                                    else if (value.Label == "Override State")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Override State", node.Level);
                                    }
                                    break;
                                case "Routing Multilevel Sensor":
                                    if (value.Label == "Temperature")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Temperature", node.Level);
                                    }
                                    else if (value.Label == "Luminance")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Luminance", node.Level);
                                    }
                                    else if (value.Label == "Battery Level")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Battery Level", node.Level);
                                    }
                                    else if (value.Label == "Power")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Power", node.Level);
                                    }
                                    else if (value.Label == "General")
                                    {
                                        node.Level = value.Val;
                                        if (Int32.Parse(node.Level) == 0)
                                            osae.ObjectStateSet(nodeObject.Name, "OFF");
                                        else
                                            osae.ObjectStateSet(nodeObject.Name, "ON");
                                    }
                                    break;
                                case "Routing Binary Sensor":
                                    if (value.Label == "Sensor")
                                    {
                                        if (value.Val == "True")
                                            osae.ObjectStateSet(nodeObject.Name, "ON");
                                        else
                                            osae.ObjectStateSet(nodeObject.Name, "OFF");
                                    }
                                    if (value.Label == "Battery Level")
                                    {
                                        node.Level = value.Val;
                                        osae.ObjectPropertySet(nodeObject.Name, "Battery Level", node.Level);
                                    }
                                    if (value.Label == "Alarm Level")
                                    {
                                        osae.ObjectPropertySet(nodeObject.Name, "Alarm Level", value.Val);
                                    }
                                    break;

                            }

                            osae.AddToLog("ValueChanged: " + ((nodeObject != null) ? nodeObject.Name : "Object Not In OSA") + " | node:"
                                + node.ID + " | nodelabel: " + node.Label + " | type: " + value.Type
                                + " | genre: " + value.Genre + " | cmdClsID:" + value.CommandClassID
                                + " | value: " + value.Val + " | label: " + value.Label, false);

                        }
                        catch (Exception ex)
                        {
                            osae.AddToLog("ValueChanged error: " + ex.Message, true);
                        }
                        break;
                    }
                #endregion

                #region Group
                case ZWNotification.Type.Group:
                    {
                        Node node = GetNode(m_homeId, m_notification.GetNodeId());
                        osae.AddToLog("Group: " + node.ID, true);
                        break;
                    }
                #endregion

                #region NodeAdded
                case ZWNotification.Type.NodeAdded:
                    {
                        // Add the new node to our list
                        Node node = new Node();
                        node.ID = m_notification.GetNodeId();
                        node.HomeID = m_homeId;
                        node.Label = m_manager.GetNodeType(m_homeId, node.ID);
                        m_nodeList.Add(node);

                        osae.AddToLog("NodeAdded: " + node.ID.ToString(), true);
                        break;
                    }
                #endregion

                #region NodeRemoved
                case ZWNotification.Type.NodeRemoved:
                    {
                        foreach (Node node in m_nodeList)
                        {
                            if (node.ID == m_notification.GetNodeId())
                            {
                                m_nodeList.Remove(node);
                                break;
                            }
                        }
                        osae.AddToLog("NodeRemoved: " + m_notification.GetNodeId(), true);
                        break;
                    }
                #endregion

                #region NodeProtocolInfo
                case ZWNotification.Type.NodeProtocolInfo:
                    {
                        Node node = GetNode(m_notification.GetHomeId(), m_notification.GetNodeId());

                        if (node != null)
                        {
                            node.Label = m_manager.GetNodeType(m_homeId, node.ID);
                        }

                        if (osae.GetObjectByAddress("Z" + node.ID.ToString()) == null)
                        {
                            switch (node.Label)
                            {
                                case "Binary Switch":
                                case "Binary Power Switch":
                                    if (m_manager.GetNodeProductName(m_homeId, node.ID) == "Smart Engery Switch")
                                        osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE SMART ENERGY SWITCH", "Z" + node.ID.ToString(), "", true);
                                    else
                                        osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE BINARY SWITCH", "Z" + node.ID.ToString(), "", true);
                                    osae.ObjectPropertySet(node.Label + " - Z" + node.ID.ToString(), "Home ID", node.HomeID.ToString());
                                    osae.ObjectStateSet(node.Label + " - Z" + node.ID.ToString(), "OFF");
                                    break;
                                case "Multilevel Switch":
                                case "Multilevel Power Switch":
                                case "Multilevel Scene Switch":
                                    if (m_manager.GetNodeProductName(m_homeId, node.ID) == "Smart Energy Illuminator")
                                        osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE SMART ENERGY DIMMER", "Z" + node.ID.ToString(), "", true);
                                    else
                                        osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE DIMMER", "Z" + node.ID.ToString(), "", true);

                                    osae.ObjectPropertySet(node.Label + " - Z" + node.ID.ToString(), "Home ID", node.HomeID.ToString());
                                    osae.ObjectStateSet(node.Label + " - Z" + node.ID.ToString(), "OFF");
                                    break;
                                case "General Thermostat V2":
                                    osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE THERMOSTAT", "Z" + node.ID.ToString(), "", true);
                                    osae.ObjectPropertySet(node.Label + " - Z" + node.ID.ToString(), "Home ID", node.HomeID.ToString());
                                    osae.ObjectStateSet(node.Label + " - Z" + node.ID.ToString(), "OFF");
                                    break;
                                case "Portable Remote Controller":
                                    osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE REMOTE", "Z" + node.ID.ToString(), "", true);
                                    osae.ObjectPropertySet(node.Label + " - Z" + node.ID.ToString(), "Home ID", node.HomeID.ToString());
                                    break;
                                case "Routing Multilevel Sensor":
                                    if (m_manager.GetNodeProductName(m_homeId, node.ID) == "Home Energy Meter")
                                        osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE HOME ENERGY METER", "Z" + node.ID.ToString(), "", true);
                                    else
                                        osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE MULTISENSOR", "Z" + node.ID.ToString(), "", true);

                                    osae.ObjectPropertySet(node.Label + " - Z" + node.ID.ToString(), "Home ID", node.HomeID.ToString());
                                    m_manager.AddAssociation(m_homeId, node.ID, 1, m_manager.GetControllerNodeId(m_homeId));
                                    break;
                                case "Routing Binary Sensor":
                                    osae.ObjectAdd(node.Label + " - Z" + node.ID.ToString(), node.Label, "ZWAVE BINARY SENSOR", "Z" + node.ID.ToString(), "", true);
                                    osae.ObjectPropertySet(node.Label + " - Z" + node.ID.ToString(), "Home ID", node.HomeID.ToString());
                                    osae.ObjectStateSet(node.Label + " - Z" + node.ID.ToString(), "OFF");
                                    m_manager.AddAssociation(m_homeId, node.ID, 1, m_manager.GetControllerNodeId(m_homeId));
                                    break;
                            }
                        }
                        osae.AddToLog("NodeProtocolInfo: node: " + node.ID + " | " + m_manager.GetNodeType(m_homeId, node.ID), true);

                        break;
                    }
                #endregion

                #region NodeNameing
                case ZWNotification.Type.NodeNaming:
                    {
                        Node node = GetNode(m_notification.GetHomeId(), m_notification.GetNodeId());
                        if (node != null)
                        {
                            node.Manufacturer = m_manager.GetNodeManufacturerName(m_homeId, node.ID);
                            node.Product = m_manager.GetNodeProductName(m_homeId, node.ID);
                        }

                        osae.AddToLog("NodeNaming: Manufacturer: " + node.Manufacturer + " | Product: " + node.Product, true);
                        break;
                    }
                #endregion

                #region NodeNew
                case ZWNotification.Type.NodeNew:
                    {
                        Node node = GetNode(m_notification.GetHomeId(), m_notification.GetNodeId());
                        if (node != null)
                        {
                            node.Manufacturer = m_manager.GetNodeManufacturerName(m_homeId, node.ID);
                            node.Product = m_manager.GetNodeProductName(m_homeId, node.ID);
                        }

                        osae.AddToLog("NodeNew: Manufacturer: " + node.Manufacturer + " | Product: " + node.Product, true);
                        break;
                    }
                #endregion

                #region NodeEvent
                case ZWNotification.Type.NodeEvent:
                    {
                        try
                        {
                            Node node = GetNode(m_homeId, m_notification.GetNodeId());
                            if (node != null)
                            {
                                node.Label = m_manager.GetNodeType(m_homeId, node.ID);
                            }
                            osae.AddToLog("---NodeEvent start: node:" + node.ID.ToString(), false);
                            osae.AddToLog("GetEvent:" + m_notification.GetEvent().ToString(), false);
                            osae.AddToLog("node.Label:" + node.Label, false);

                            ZWValueID vid = m_notification.GetValueID();
                            Value value = node.GetValue(vid);
                            OSAEObject nodeObject = osae.GetObjectByAddress("Z" + m_notification.GetNodeId());
                            string v;
                            m_manager.GetValueAsString(vid, out v);
                            value.Val = v;

                            switch (node.Label)
                            {
                                case "Routing Binary Sensor":
                                    if (m_notification.GetEvent().ToString() == "255" || m_notification.GetEvent().ToString() == "99")
                                    {
                                        osae.ObjectStateSet(nodeObject.Name, "ON");
                                        osae.AddToLog("Sensor turned ON: " + nodeObject.Name, false);
                                    }
                                    else if (m_notification.GetEvent().ToString() == "0")
                                    {
                                        osae.ObjectStateSet(nodeObject.Name, "OFF");
                                        osae.AddToLog("Sensor turned OFF: " + nodeObject.Name, false);
                                    }

                                    //if (value.Label == "Sensor")
                                    //{
                                    //    if (value.Val == "True")
                                    //    {
                                    //        osae.ObjectStateSet(nodeObject.Name, "ON");
                                    //        osae.AddToLog("Sensor turned ON: " + nodeObject.Name, false);
                                    //    }

                                    //    else
                                    //    {
                                    //        osae.ObjectStateSet(nodeObject.Name, "OFF");
                                    //        osae.AddToLog("Sensor turned OFF: " + nodeObject.Name, false);
                                    //    }
                                    //}
                                    break;
                                case "Routing Multilevel Sensor":
                                    if (m_notification.GetEvent().ToString() == "255" || m_notification.GetEvent().ToString() == "99")
                                    {
                                        osae.ObjectStateSet(nodeObject.Name, "ON");
                                        osae.AddToLog("Sensor turned ON: " + nodeObject.Name, false);
                                    }
                                    else if (m_notification.GetEvent().ToString() == "0")
                                    {
                                        osae.ObjectStateSet(nodeObject.Name, "OFF");
                                        osae.AddToLog("Sensor turned OFF: " + nodeObject.Name, false);
                                    }
                                    break;
                            }
                            osae.AddToLog("NodeEvent: " + ((nodeObject != null) ? nodeObject.Name : "Object Not In OSA") + " | node:" + node.ID + " | type: " + value.Type
                            + " | genre: " + value.Genre + " | cmdClsID:" + value.CommandClassID
                            + " | value: " + value.Val + " | label: " + value.Label, false);
                        }
                        catch (Exception ex)
                        {
                            osae.AddToLog("Error in NodeEvent: " + ex.Message, true);
                        }

                        break;
                    }
                #endregion

                #region PollingDisabled
                case ZWNotification.Type.PollingDisabled:
                    {
                        break;
                    }
                #endregion

                #region PollingEnabled
                case ZWNotification.Type.PollingEnabled:
                    {
                        osae.AddToLog("Polling Enabled: " + osae.GetObjectByAddress("Z" + m_notification.GetNodeId().ToString()).Name, true);
                        break;
                    }
                #endregion

                #region DriverReady
                case ZWNotification.Type.DriverReady:
                    {
                        m_homeId = m_notification.GetHomeId();
                        osae.ObjectPropertySet(pName, "Home ID", m_homeId.ToString());
                        osae.AddToLog("Driver Ready.  Home ID: " + m_homeId.ToString(), true);
                        break;
                    }
                #endregion

                #region DriverReset
                case ZWNotification.Type.DriverReset:
                    {
                        m_homeId = m_notification.GetHomeId();
                        osae.AddToLog("Driver Reset.  Home ID: " + m_homeId.ToString(), true);
                        break;
                    }
                #endregion

                #region NodeQueriesComplete
                case ZWNotification.Type.NodeQueriesComplete:
                    {
                        Node node = GetNode(m_notification.GetHomeId(), m_notification.GetNodeId());


                        osae.AddToLog("Node Queries Complete | " + node.ID + " | " + m_manager.GetNodeProductName(m_homeId, node.ID), true);
                        break;
                    }
                #endregion

                #region EssentialNodeQueriesComplete
                case ZWNotification.Type.EssentialNodeQueriesComplete:
                    {
                        Node node = GetNode(m_homeId, m_notification.GetNodeId());
                        osae.AddToLog("Essential Node Queries Completee | " + node.ID + " | " + m_manager.GetNodeProductName(m_homeId, node.ID), true);
                        break;
                    }
                #endregion

                #region AllNodesQueried
                case ZWNotification.Type.AllNodesQueried:
                    {
                        osae.AddToLog("All nodes queried", true);
                        foreach (Node n in m_nodeList)
                        {
                            OSAEObject obj = osae.GetObjectByAddress("Z" + n.ID.ToString());
                            if (obj != null)
                            {
                                if (osae.GetObjectPropertyValue(osae.GetObjectByAddress("Z" + n.ID.ToString()).Name, "Poll").Value == "TRUE")
                                    enablePolling(n.ID);
                            }
                        }
                        break;
                    }
                #endregion

                #region AwakeNodesQueried
                case ZWNotification.Type.AwakeNodesQueried:
                    {
                        osae.AddToLog("Awake nodes queried (but some sleeping nodes have not been queried)", true);
                        foreach (Node n in m_nodeList)
                        {
                            OSAEObject obj = osae.GetObjectByAddress("Z" + n.ID.ToString());
                            if (obj != null)
                            {
                                if (osae.GetObjectPropertyValue(osae.GetObjectByAddress("Z" + n.ID.ToString()).Name, "Poll").Value == "TRUE")
                                {
                                    osae.AddToLog("Enabling polling for: " + obj.Name, true);
                                    enablePolling(n.ID);
                                }
                            }
                        }
                        break;
                    }
                #endregion
            }

        }

        public static void MyControllerStateChangedHandler(ZWControllerState state)
        {
            // Handle the controller state notifications here.
            bool complete = false;
            switch (state)
            {
                case ZWControllerState.Waiting:
                    {
                        osae.AddToLog("Waiting...", true);
                        break;
                    }
                case ZWControllerState.InProgress:
                    {
                        // Tell the user that the controller has been found and the adding process is in progress.
                        osae.AddToLog("Please wait...", true);
                        break;
                    }
                case ZWControllerState.Completed:
                    {
                        // Tell the user that the controller has been successfully added.
                        // The command is now complete
                        osae.AddToLog("Command Completed OK.", true);
                        complete = true;
                        break;
                    }
                case ZWControllerState.Failed:
                    {
                        // Tell the user that the controller addition process has failed.
                        // The command is now complete
                        osae.AddToLog("Command Failed.", true);
                        complete = true;
                        break;
                    }
                case ZWControllerState.NodeOK:
                    {
                        osae.AddToLog("Node has not failed.", true);
                        complete = true;
                        break;
                    }
                case ZWControllerState.NodeFailed:
                    {
                        osae.AddToLog("Node has failed.", true);
                        complete = true;
                        break;
                    }
            }


            if (complete)
            {
                osae.AddToLog("Removing event handler", true);
                // Remove the event handler
                m_manager.OnControllerStateChanged -= m_controllerStateChangedHandler;
            }
        }


        private Node GetNode(UInt32 homeId, Byte nodeId)
        {
            foreach (Node node in m_nodeList)
            {
                if ((node.ID == nodeId) && (node.HomeID == homeId))
                {
                    return node;
                }
            }

            return new Node();
        }

        private void enablePolling(byte nid)
        {
            osae.AddToLog("Attempting to Enable Polling: " + osae.GetObjectByAddress("Z" + nid.ToString()).Name, true);
            try
            {
                Node n = GetNode(m_homeId, nid);
                List<ZWValueID> zv = new List<ZWValueID>();
                switch (n.Label)
                {
                    case "Binary Switch":
                    case "Binary Power Switch":
                        foreach (Value v in n.Values)
                        {
                            if (v.Label == "Switch")
                                zv.Add(v.ValueID);
                        }
                        break;
                    case "Multilevel Switch":
                    case "Multilevel Power Switch":
                        foreach (Value v in n.Values)
                        {
                            if ((v.Genre == "User" && v.Label == "Level") || v.Label == "Power")
                                zv.Add(v.ValueID);
                        }
                        break;
                    case "General Thermostat V2":
                        foreach (Value v in n.Values)
                        {
                            if (v.Label == "Temperature")
                                zv.Add(v.ValueID);
                        }
                        break;
                    case "Routing Multilevel Sensor":
                        if (m_manager.GetNodeProductName(m_homeId, n.ID) == "Smart Energy Switch")
                        {
                            foreach (Value v in n.Values)
                            {
                                if (v.Label == "Power")
                                    zv.Add(v.ValueID);
                            }
                        }
                        break;
                }
                foreach (ZWValueID zwv in zv)
                {
                    if (m_manager.EnablePoll(zwv))
                        osae.AddToLog("Enable Polling Succeeded", true);
                    else
                        osae.AddToLog("Enable Polling Failed", true);
                }
            }
            catch (Exception ex)
            {
                osae.AddToLog("Error attempting to enable polling: " + ex.Message, true);
            }
        }
    }
}
