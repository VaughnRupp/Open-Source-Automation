﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;  
using System.ServiceModel;

namespace WCF
{
  

    interface IMessageCallback
    {
        [OperationContract(IsOneWay = true)]
        void OnMessageReceived(string msgType, string message, string from, DateTime timestamp);
    }
}
