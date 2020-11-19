using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core.Data;
using BEditor.Core.Service;

namespace BEditor.Core.Extensions
{
    public static class ActivityLog
    {
        private static readonly string xmlpath = $"{Services.Path}\\user\\logs\\errorlog.xml";
        public static void ErrorLog(Exception e)
        {
            if (!Settings.Default.EnableErrorLog) return;
            Task.Run(() =>
            {
                var xdoc = XDocument.Load(xmlpath);

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source),
                    new XElement("StackTrace", e.StackTrace)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(xmlpath);
            });
        }

        public static void ErrorLog(Exception e, string message)
        {
            if (!Settings.Default.EnableErrorLog) return;

            Task.Run(() =>
            {
                var xdoc = XDocument.Load(xmlpath);

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source),
                    new XElement("Message", message),
                    new XElement("StackTrace", e.StackTrace)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(xmlpath);
            });
        }
        public static void ErrorLogStackTrace(Exception e, string stackTrace)
        {
            if (!Settings.Default.EnableErrorLog) return;

            Task.Run(() =>
            {
                var xdoc = XDocument.Load(xmlpath);

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source),
                    new XElement("StackTrace", stackTrace)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(xmlpath);
            });
        }
    }
}
