using System;
using System.Net;
using System.Web;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Linq;
using System.Text;

namespace RecipeServer
{
    public class WebServer
    {
        private Dictionary<string, SessionInfo> sessions_ = new Dictionary<string, SessionInfo>();

        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerRequest, HttpListenerResponse, SessionInfo, string> _responderMethod;

        public WebServer(Func<HttpListenerRequest, HttpListenerResponse, SessionInfo, string> method, params string[] prefixes)
        {
            if (!HttpListener.IsSupported)
                throw new NotSupportedException("Needs Windows XP SP2, Server 2003 or later.");
 
            // URI prefixes are required, for example 
            // "http://localhost:8080/index/"
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");
 
            // A responder method is required
            if (method == null)
                throw new ArgumentException("method");
 
            foreach (string s in prefixes)
                _listener.Prefixes.Add(s);

            _responderMethod = method;
            _listener.Start();
        }

        public void Run()
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                Console.WriteLine("Webserver running...");
                try
                {
                    while (_listener.IsListening)
                    {
                        HttpListenerContext context = _listener.GetContext();
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                string sessionId = RequireSessionId(context);
                                SessionInfo sessionInfo = null;
                                lock (sessions_)
                                {
                                    if (ctx.Request.Url.LocalPath == "/logout")
                                        sessions_.Remove(sessionId); // forget the previous session
                                    if (!sessions_.TryGetValue(sessionId, out sessionInfo))
                                        sessions_[sessionId] = sessionInfo = new SessionInfo(sessionId);
                                }
                                string rsp = _responderMethod(ctx.Request, ctx.Response, sessionInfo);
                                if (null != rsp)
                                {
                                    // have to serialize the response, if the _responseMethod didn't do it
                                    byte[] buf = Encoding.UTF8.GetBytes(rsp);
                                    ctx.Response.ContentLength64 = buf.Length;
                                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                                }
                            }
                            catch (Exception e)
                            {
                                Console.Error.WriteLine("Minor web server error: {0}. Ignoring and proceeding...", e);
                            }
                            finally
                            {
                                // always close the stream
                                ctx.Response.OutputStream.Close();
                            }
                        },
                        context);
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Major web server error: {0}. Shutting down...", e);
                }
            });
        }

        private string RequireSessionId(HttpListenerContext ctx)
        {
            string sessionId = null;
            foreach (Cookie cookie in ctx.Request.Cookies)
            {
                if (cookie.Name == "JSESSIONID")
                {
                    sessionId = cookie.Value;
                    break;
                }
            }
            if (null == sessionId)
            {
                bool sessionIdUnique;
                do
                {
                    Random r = new Random();
                    StringBuilder randomSessionId = new StringBuilder();
                    while (randomSessionId.Length < 48)
                        randomSessionId.Append((char)('A' + r.Next('z' - 'A')));
                    sessionId = randomSessionId.ToString();
                    lock (sessions_)
                    {
                        sessionIdUnique = !sessions_.ContainsKey(sessionId);
                    }
                }
                while (!sessionIdUnique);
                ctx.Response.SetCookie(new Cookie("JSESSIONID", sessionId));
            }
            return sessionId;
        }
 
        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }

    }
}