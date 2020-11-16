using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core.Data;

namespace BEditor.Core.Extensions
{
    public static class ActivityLog
    {
        public static void ErrorLog(Exception e)
        {
            if (!Settings.Default.EnableErrorLog) return;
            var app = Component.Funcs.GetApp();
            Task.Run(() =>
            {
                var xdoc = XDocument.Load(app.Path + "\\user\\logs\\errorlog.xml");

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(app.Path + "\\user\\logs\\errorlog.xml");
            });
        }

        public static void ErrorLog(Exception e, string message)
        {
            if (!Settings.Default.EnableErrorLog) return;

            var app = Component.Funcs.GetApp();
            Task.Run(() =>
            {
                var xdoc = XDocument.Load(app.Path + "\\user\\logs\\errorlog.xml");

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source),
                    new XElement("Message", message)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(app.Path + "\\user\\logs\\errorlog.xml");
            });
        }
    }
}
