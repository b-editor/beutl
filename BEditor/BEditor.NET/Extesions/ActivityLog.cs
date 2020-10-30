using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.NET.Data;

namespace BEditor.NET.Extensions {
    public class ActivityLog {
        public static void ErrorLog(Exception e) {
            if (!Component.Settings.EnableErrorLog?.Invoke() ?? false) return;

            Task.Run(() => {
                var xdoc = XDocument.Load(Component.Current.Path + "\\user\\logs\\errorlog.xml");

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(Component.Current.Path + "\\user\\logs\\errorlog.xml");
            });
        }

        public static void ErrorLog(Exception e, string message) {
            if (!Component.Settings.EnableErrorLog?.Invoke() ?? false) return;

            Task.Run(() => {
                var xdoc = XDocument.Load(Component.Current.Path + "\\user\\logs\\errorlog.xml");

                var xelm = new XElement("Error",
                    new XElement("ExceptionMessage", e.Message),
                    new XElement("Source", e.Source),
                    new XElement("Message", message)
                );

                xdoc.Elements().First().Add(xelm);

                xdoc.Save(Component.Current.Path + "\\user\\logs\\errorlog.xml");
            });
        }
    }
}
