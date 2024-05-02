/* ====================================================================
   Copyright (C) 2004-2008  fyiReporting Software, LLC
   Copyright (C) 2011  Peter Gill <peter@majorsilence.com>

   This file is part of the fyiReporting RDL project.
	
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.


   For additional information, email info@fyireporting.com or visit
   the website www.fyiReporting.com.
*/

using System;
using System.IO;
using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Web.Caching;
using fyiReporting.RDL;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;
using System.Collections.Generic;

namespace fyiReporting.RdlAsp
{
    /// <summary>
    /// Summary description for Class1.
    /// </summary>
    public class RdlReport : Controller
    {
        /// <summary>
        /// RdlReport generates an HTML report from a RDL file.
        /// </summary>
        /// 
        private const string STATISTICS = "statistics";
        private string _ReportFile = null;
        private ArrayList _Errors = null;
        private int _MaxSeverity = 0;
        private string _CSS = null;
        private string _JavaScript = null;
        private string _Html = null;
        private string _Xml = null;
        private string _Csv = null;
        private byte[] _Object = null;
        private string _ParameterHtml = null;
        private OutputPresentationType _RenderType = OutputPresentationType.ASPHTML;
        private string _PassPhrase = null;
        private bool _NoShow;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IMemoryCache _cache;

        public RdlReport(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
        {
            _webHostEnvironment = webHostEnvironment;
            _cache = cache;
        }

        public ContentResult Render()
        {
            var htmlContent = new StringBuilder();
            if (_ReportFile == null)
            {
                this.AddError(8, "ReportFile not specified.");
                return Content("");
            }
            else if (_ReportFile == STATISTICS)
            {
                DoStatistics(ref htmlContent);
                return Content(htmlContent.ToString());
            }
            else if (_Html != null)
                htmlContent.AppendLine(_Html);
            else if (_Object != null)
            {
                // TODO -   shouldn't use control to write out object???
                throw new Exception("_Object needed in render");
            }
            else    // we never generated anything!
            {
                if (_Errors != null)
                {
                    htmlContent.AppendLine("<table>");
                    htmlContent.AppendLine("<tr>");
                    htmlContent.AppendLine("<td>");
                    htmlContent.AppendLine("Errors");
                    htmlContent.AppendLine("</td>");
                    htmlContent.AppendLine("</tr>");

                    foreach (string e in _Errors)
                    {
                        htmlContent.AppendLine("<tr>");
                        htmlContent.AppendLine("<td>");
                        htmlContent.AppendLine(e);
                        htmlContent.AppendLine("</td>");
                        htmlContent.AppendLine("</tr>");
                    }
                    htmlContent.AppendLine("</table>");

                }
            }

            return Content(htmlContent.ToString());
        }
        /// <summary>
        /// When true report won't be shown but parameters (if any) will be
        /// </summary>
        public bool NoShow
        {
            get { return _NoShow; }
            set { _NoShow = value; }
        }
        public string RenderType
        {
            get
            {
                switch (_RenderType)
                {
                    case OutputPresentationType.ASPHTML:
                    case OutputPresentationType.HTML:
                        return "html";
                    case OutputPresentationType.PDF:
                        return "pdf";
                    case OutputPresentationType.XML:
                        return "xml";
                    case OutputPresentationType.CSV:
                        return "csv";
                    case OutputPresentationType.ExcelTableOnly:
                    case OutputPresentationType.Excel2007:
                        return "xlsx";
                    case OutputPresentationType.RTF:
                        return "rtf";
                    default:
                        return "html";
                }
            }
            set
            {
                _RenderType = this.GetRenderType(value);
            }
        }

        public string ReportFile
        {
            get { return _ReportFile; }
            set
            {
                _ReportFile = value;
                // Clear out old report information (if any)
                this._Errors = null;
                this._MaxSeverity = 0;
                _CSS = null;
                _JavaScript = null;
                _Html = null;
                _ParameterHtml = null;

                if (_ReportFile == STATISTICS)
                {
                    var sb = new StringBuilder();

                    DoStatistics(ref sb);
                    _Html = sb.ToString();

                    return;
                }

                // Build the new report
                string contentRootPath = _webHostEnvironment.ContentRootPath;
                string pfile = Path.Combine(contentRootPath, _ReportFile);
                DoRender(pfile);
            }
        }

        public string PassPhrase
        {
            set { _PassPhrase = value; }
        }

        private string GetPassword()
        {
            return _PassPhrase;
        }


        public string Html
        {
            get { return _Html; }
        }

        public string Xml
        {
            get { return _Xml; }
        }

        public string CSV
        {
            get { return _Csv; }
        }

        public byte[] Object
        {
            get { return _Object; }
        }

        public ArrayList Errors
        {
            get { return _Errors; }
        }

        public int MaxErrorSeverity
        {
            get { return _MaxSeverity; }
        }

        public string CSS
        {
            get { return _CSS; }
        }

        public string JavaScript
        {
            get { return _JavaScript; }
        }

        public string ParameterHtml
        {
            get
            {
                return _ParameterHtml;
            }
        }


        // Render the report files with the requested types
        private void DoRender(string file)
        {

            string source;
            Report report = null;

            var nvc = this.HttpContext.Request.Query;       // parameters
            ListDictionary ld = new ListDictionary();
            try
            {
                foreach (var kvp in nvc)
                {
                    ld.Add(kvp.Key, kvp.Value);
                }

                //               if (!_NoShow) { report = GetCachedReport(file); }
                report = ReportHelper.GetCachedReport(file, _cache);

                if (report == null) // couldn't obtain report definition from cache
                {
                    // Obtain the source
                    source = ReportHelper.GetSource(file);
                    if (source == null)
                        return;                 // GetSource reported the error

                    // Compile the report
                    report = this.GetReport(source, file);
                    if (report == null)
                        return;

                    ReportHelper.SaveCachedReport(report, file, _cache);
                }
                // Set the user context information: ID, language
                ReportHelper.SetUserContext(report, this.HttpContext, new RDL.NeedPassword(GetPassword));

                // Obtain the data if report is being generated
                if (!_NoShow)
                {
                    report.RunGetData(ld);
                    Generate(report);
                }
            }
            catch (Exception exe)
            {
                AddError(8, "Error: {0}", exe.Message);
            }

            if (_ParameterHtml == null)
                _ParameterHtml = ReportHelper.GetParameterHtml(report, ld, this.HttpContext, _ReportFile, _NoShow); // build the parameter html

        }

        private void AddError(int severity, string err, params object[] args)
        {
            if (_MaxSeverity < severity)
                _MaxSeverity = severity;

            string error = string.Format(err, args);
            if (_Errors == null)
                _Errors = new ArrayList();
            _Errors.Add(error);
        }

        private void AddError(int severity, IList errors)
        {
            if (_MaxSeverity < severity)
                _MaxSeverity = severity;
            if (_Errors == null)
            {   // if we don't have any we can just start with this list
                _Errors = new ArrayList(errors);
                return;
            }

            // Need to copy all items in the errors array
            foreach (string err in errors)
                _Errors.Add(err);

            return;
        }

        private void DoStatistics(ref StringBuilder htmlContent)
        {
            RdlSession rs = _cache.Get(RdlSession.SessionStat) as RdlSession;
            ReportHelper s = ReportHelper.Get(_cache);
            IMemoryCache c = _cache;

            int sessions = 0;
            if (rs != null)
                sessions = rs.Count;


            var cacheEntries = GetCacheEntries(c);
            htmlContent.AppendLine($"<p>{sessions} sessions");
            htmlContent.AppendLine($"<p>{cacheEntries.Count} items are in the cache");
            htmlContent.AppendLine($"<p>{s.CacheHits} cache hits");
            htmlContent.AppendLine($"<p>{s.CacheMisses} cache misses");

            foreach (var de in cacheEntries)
            {
                /*
				if (de.Value is ReportDefn)
                    htmlContent.AppendLine("<p>file=" + de);
				else
                    htmlContent.AppendLine("<p>key=" + de);
				*/
            }
        }

        private List<string> GetCacheEntries(IMemoryCache cache)
        {
            var field = cache.GetType().GetProperty("EntriesCollection", BindingFlags.NonPublic | BindingFlags.Instance);
            var collection = field.GetValue(cache) as ICollection;
            var items = new List<string>();
            if (collection != null)
                foreach (var item in collection)
                {
                    var methodInfo = item.GetType().GetProperty("Key");
                    var val = methodInfo.GetValue(item);
                    items.Add(val.ToString());
                }

            return items;
        }

        private void Generate(Report report)
        {
            MemoryStreamGen sg = null;
            try
            {
                sg = new MemoryStreamGen("ShowFile?type=", null, this.RenderType);

                report.RunRender(sg, _RenderType, Guid.NewGuid().ToString());
                _CSS = "";
                _JavaScript = "";
                switch (_RenderType)
                {
                    case OutputPresentationType.ASPHTML:
                    case OutputPresentationType.HTML:
                        _CSS = report.CSS;//.Replace("position: relative;", "position: absolute;");
                        _JavaScript = report.JavaScript;
                        _Html = sg.GetText();
                        break;
                    case OutputPresentationType.XML:
                        _Xml = sg.GetText();
                        break;
                    case OutputPresentationType.CSV:
                        _Csv = sg.GetText();
                        break;
                    case OutputPresentationType.PDF:
                        {
                            MemoryStream ms = sg.MemoryList[0] as MemoryStream;
                            _Object = ms.ToArray();
                            break;
                        }
                }

                // Now save off the other streams in the session context for later use
                IList strms = sg.MemoryList;
                IList names = sg.MemoryNames;
                for (int i = 1; i < sg.MemoryList.Count; i++)   // we skip the first one
                {
                    string n = names[i] as string;
                    MemoryStream ms = strms[i] as MemoryStream;
                    HttpContext.Session.Set(n, ms.ToArray());
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                if (sg != null)
                {
                    sg.CloseMainStream();
                }
            }

            if (report.ErrorMaxSeverity > 0)
            {
                AddError(report.ErrorMaxSeverity, report.ErrorItems);
                report.ErrorReset();
            }

            return;
        }

        private OutputPresentationType GetRenderType(string type)
        {
            switch (type.ToLower())
            {
                case "htm":
                case "html":
                    return OutputPresentationType.ASPHTML;
                case "pdf":
                    return OutputPresentationType.PDF;
                case "xml":
                    return OutputPresentationType.XML;
                case "csv":
                    return OutputPresentationType.CSV;
                case "xlsx":
                    return OutputPresentationType.ExcelTableOnly;
                case "rtf":
                    return OutputPresentationType.RTF;
                default:
                    return OutputPresentationType.ASPHTML;
            }
        }


        private Report GetReport(string prog, string file)
        {
            // Now parse the file
            RDLParser rdlp;
            Report r;
            try
            {
                // Make sure RdlEngine is configed before we ever parse a program
                //   The config file must exist in the Bin directory.
                string searchDir = this.ReportFile.StartsWith("~") ? "~/Bin" : "/Bin" + Path.DirectorySeparatorChar;
                RdlEngineConfig.RdlEngineConfigInit(searchDir);

                rdlp = new RDLParser(prog);
                string folder = Path.GetDirectoryName(file);
                if (folder == "")
                    folder = Environment.CurrentDirectory;
                rdlp.Folder = folder;
                rdlp.DataSourceReferencePassword = new NeedPassword(this.GetPassword);

                r = rdlp.Parse();
                if (r.ErrorMaxSeverity > 0)
                {
                    AddError(r.ErrorMaxSeverity, r.ErrorItems);
                    if (r.ErrorMaxSeverity >= 8)
                        r = null;
                    r.ErrorReset();
                }

                // If we've loaded the report; we should tell it where it got loaded from
                if (r != null)
                {
                    r.Folder = folder;
                    r.Name = Path.GetFileNameWithoutExtension(file);
                    r.GetDataSourceReferencePassword = new RDL.NeedPassword(GetPassword);
                }
            }
            catch (Exception e)
            {
                r = null;
                AddError(8, "Exception parsing report {0}.  {1}", file, e.Message);
            }
            return r;
        }
    }
}
