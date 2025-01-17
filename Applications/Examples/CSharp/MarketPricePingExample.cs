﻿//|-----------------------------------------------------------------------------
//|            This source code is provided under the Apache 2.0 license      --
//|  and is provided AS IS with no warranty or guarantee of fit for purpose.  --
//|                See the project's LICENSE.md for details.                  --
//|            Copyright (C) 2017-2020 Refinitiv. All rights reserved.        --
//|-----------------------------------------------------------------------------


using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using WebSocketSharp;

/*
 * This example demonstrates retrieving JSON-formatted market content from a WebSocket server.
 * It performs the following steps:
 * - Logs into the WebSocket server.
 * - Requests TRI.N market-price content.
 * - Prints the response content.
 * - Sends Ping messages to monitor connection health.
 */

namespace MarketPricePingExample
{
    class MarketPriceExample
    {
        /// <summary>The websocket used for retrieving market content.</summary>
        private WebSocket _webSocket;

        /// <summary>Indicates whether we have successfully logged in.</summary>
        private bool _loggedIn = false;

        /// <summary>Interval for disconnecting from ping timeout.</summary>
        private long _pingTimeoutIntervalMs = 30000;

        /// <summary>The next time at which we should send a ping to the server.</summary>
        private long _pingSendTimeMs;

        /// <summary>If we send a Ping but do not receive a Pong in response by this time, exit.</summary>
        private long _pingTimeoutTimeMs;

        /// <summary>Tracks the current time.</summary>
        private Stopwatch _stopwatch;

        /// <summary>The configured hostname of the Websocket server.</summary>
        private string _hostName = "localhost";

        /// <summary>The configured port used when opening the WebSocket.</summary>
        private string _port = "15000";

        /// <summary>The configured username used when logging in.</summary>
        private string _userName = Environment.UserName;

        /// <summary>The configured ApplicationID used when logging in.</summary>
        private string _appId = "256";

        /// <summary>The IP address, used as the application's position when logging in.</summary>
        private string _position;

        /// <summary>Parses commandline config and runs the application.</summary>
        static void Main(string[] args)
        {
            MarketPriceExample example = new MarketPriceExample();
            example.parseCommandLine(args);
            example.run();
        }

        /// <summary>Runs the application. Opens the WebSocket.</summary>
        public void run()
        {
            /* Get local hostname. */
            IPAddress hostEntry = Array.Find(Dns.GetHostEntry(Dns.GetHostName()).AddressList, ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (hostEntry != null)
                _position = hostEntry.ToString();
            else
                _position = "127.0.0.1";

            /* Open a websocket. */
            string hostString = "ws://" + _hostName + ":" + _port + "/WebSocket";
            Console.Write("Connecting to WebSocket " + hostString + " ...");
            _webSocket = new WebSocket(hostString, "tr_json2");

            _webSocket.OnOpen += onWebSocketOpened;
            _webSocket.OnError += onWebSocketError;
            _webSocket.OnClose += onWebSocketClosed;
            _webSocket.OnMessage += onWebSocketMessage;

            /* Print any log events (similar to default behavior, but we explicitly indicate it's a logger event). */
            _webSocket.Log.Output = (logData, text) => Console.WriteLine("Received Log Event (Level: {0}): {1}\n", logData.Level, logData.Message);

            _webSocket.Connect();

            _stopwatch = new Stopwatch();
            _stopwatch.Start();

            /* Received a message or a ping; update ping timeout time. */
            _pingTimeoutTimeMs = 0;
            _pingSendTimeMs = _stopwatch.ElapsedMilliseconds + _pingTimeoutIntervalMs / 3;
            while (true)
            {
                Thread.Sleep(1000);
                /* If we haven't recieved traffic in some time, send a Ping to confirm the connection is alive. */
                if (_pingSendTimeMs > 0 && _stopwatch.ElapsedMilliseconds >= _pingSendTimeMs)
                {
                    sendMessage( "{ \"Type\":\"Ping\"}");
                    _pingSendTimeMs = 0;
                    _pingTimeoutTimeMs = _stopwatch.ElapsedMilliseconds + _pingTimeoutIntervalMs;
                }

                /* If we sent a Ping, but did not receieve a Pong in response, exit. */
                if (_pingTimeoutTimeMs > 0 && _stopwatch.ElapsedMilliseconds >= _pingTimeoutTimeMs)
                {
                    Console.WriteLine("Server ping timed out.");
                    Environment.Exit(1);
                }
            }
        }

        /// <summary>Handles the initial open of the WebSocket. Sends login request.</summary>
        private void onWebSocketOpened(object sender, EventArgs e)
        {
            Console.WriteLine("WebSocket successfully connected!\n");

            sendMessage(
                    "{"
                    + "\"ID\": 1,"
                    + "\"Domain\":\"Login\", "
                    + "\"Key\":{"
                    + "\"Name\":\"" + _userName + "\", "
                    + "\"Elements\":{"
                    + "\"ApplicationId\":\"" + _appId + "\", "
                    + "\"Position\": \"" + _position + "\""
                    + "}"
                    + "}"
                    + "}"
                    );
        }

        /// <summary>Handles messages received on the websocket.</summary>
        private void onWebSocketMessage(object sender, MessageEventArgs e)
        {
            /* Received message(s). */
            int index = 0;
            JArray messages = JArray.Parse(e.Data);

            /* Print the message (format the object string for easier reading). */
            string prettyJson = JsonConvert.SerializeObject(messages, Formatting.Indented);
            Console.WriteLine("RECEIVED:\n{0}\n", prettyJson);

            for(index = 0; index < messages.Count; ++index)
                processJsonMsg(messages[index]);

            /* Received a message or a ping; update ping timeout time. */
            _pingTimeoutTimeMs = 0;
            _pingSendTimeMs = _stopwatch.ElapsedMilliseconds + _pingTimeoutIntervalMs / 3;
        }

        /// <summary>
        /// Processes the received message. If the message is a login response indicating we are now logged in,
        /// opens a stream for TRI.N content.
        /// </summary>
        /// <param name="msg">The received JSON message</param>
        void processJsonMsg(dynamic msg)
        {
            switch((string)msg["Type"])
            {
                case "Refresh":
                    switch ((string)msg["Domain"])
                    {
                        case "Login":
                            if (msg["State"] != null && msg["State"]["Stream"] != "Open")
                            {
                                Console.WriteLine("Login stream was closed.\n");
                                Environment.Exit(1);
                            }

                            if (!_loggedIn && (msg["State"] == null || msg["State"]["Data"] == "Ok"))
                            {
                                /* Login was successful. */
                                _loggedIn = true;

                                _pingTimeoutIntervalMs = msg["Elements"]["PingTimeout"]  * 1000;

                                /* Request an item. */
                                sendMessage(
                                    "{"
                                    + "\"ID\": 2,"
                                    + "\"Key\": {\"Name\":\"TRI.N\"}"
                                    + "}"
                                    );
                            }
                            break;

                        default:
                            break;
                    }
                    break;
                case "Ping":
                    sendMessage("{\"Type\":\"Pong\"}");
                    break;
                default:
                    break;
            }

        }

        /// <summary>Prints the outbound message and sends it on the WebSocket.</summary>
        /// <param name="jsonMsg">Message to send</param>
        void sendMessage(string jsonMsg)
        {
            /* Print the message (format the object string for easier reading). */
            dynamic msg = JsonConvert.DeserializeObject(jsonMsg);
            string prettyJson = JsonConvert.SerializeObject(msg, Formatting.Indented);
            Console.WriteLine("SENT:\n{0}\n", prettyJson);

            _webSocket.Send(jsonMsg);
        }


        /// <summary>Handles the WebSocket closing.</summary>
        private void onWebSocketClosed(object sender, CloseEventArgs e)
        {
            Console.WriteLine("WebSocket was closed: {0}\n", e.Reason);
            Environment.Exit(1);
        }

        /// <summary>Handles any error that occurs on the WebSocket.</summary>
        private void onWebSocketError(object sender, ErrorEventArgs e)
        {
            Console.WriteLine("Received Error: {0}\n", e.Exception.ToString());
            Environment.Exit(1);
        }

        /// <summary>
        /// This gets called when an option that requires an argument is called
        /// without one. Prints usage and exits with a failure status.
        /// </summary>
        /// <param name="option"></param>
        void GripeAboutMissingOptionArgumentAndExit(string option)
        {
            Console.WriteLine("Error: {0} requires an argument.", option);
            PrintCommandLineUsageAndExit(1);
        }

        /// <summary>Parses command-line arguments.</summary>
        /// <param name="args">Command-line arguments passed to the application.</param>
        void parseCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                switch (args[i])
                {
                    case "-a":
                    case "--app_id":
                        if (i + 1 >= args.Length)
                            GripeAboutMissingOptionArgumentAndExit(args[i]);
                        _appId = args[i + 1];
                        ++i;
                        break;

                    case "-h":
                    case "--hostname":
                        if (i + 1 >= args.Length)
                            GripeAboutMissingOptionArgumentAndExit(args[i]);
                        _hostName = args[i + 1];
                        ++i;
                        break;

                    case "-p":
                    case "--port":
                        if (i + 1 >= args.Length)
                            GripeAboutMissingOptionArgumentAndExit(args[i]);
                        _port = args[i + 1];
                        ++i;
                        break;

                    case "-u":
                    case "--user":
                        if (i + 1 >= args.Length)
                            GripeAboutMissingOptionArgumentAndExit(args[i]);
                        _userName = args[i + 1];
                        ++i;
                        break;

                    case "--help":
                        PrintCommandLineUsageAndExit(0);
                        break;

                    default:
                        Console.WriteLine("Unknown option: {0}", args[i]);
                        PrintCommandLineUsageAndExit(1);
                        break;

                }
            }
        }

        /// <summary>Prints usage information. Used when arguments cannot be parsed.</summary>
        void PrintCommandLineUsageAndExit(int exitStatus)
        {
            Console.WriteLine("Usage:\n" +
                "dotnet {0}.dll\n" +
                "    [-h|--hostname <hostname>]    \n" +
                "    [-p|--port <port>]            \n" +
                "    [-a|--appID <appID>]          \n" +
                "    [-u|--user <user>]            \n" +
                "    [--help]                      \n",
                System.AppDomain.CurrentDomain.FriendlyName);
            Environment.Exit(exitStatus);
        }
    }
}
