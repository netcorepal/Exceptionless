﻿#region Copyright 2014 Exceptionless

// This program is free software: you can redistribute it and/or modify it 
// under the terms of the GNU Affero General Public License as published 
// by the Free Software Foundation, either version 3 of the License, or 
// (at your option) any later version.
// 
//     http://www.gnu.org/licenses/agpl-3.0.html

#endregion

using System;
using System.Threading.Tasks;
using CodeSmith.Core.Component;
using CodeSmith.Core.Extensions;
using NLog.Fluent;
using ServiceStack.CacheAccess;

namespace Exceptionless.Core.Pipeline {
    [Priority(5)]
    public class ThrottleBotsAction : EventPipelineActionBase {
        private readonly ICacheClient _cacheClient;
        private readonly EventRepository _eventRepository;
        private readonly TimeSpan _throttlingPeriod = TimeSpan.FromMinutes(5);

        public ThrottleBotsAction(ICacheClient cacheClient, EventRepository eventRepository) {
            _cacheClient = cacheClient;
            _eventRepository = eventRepository;
        }

        protected override bool ContinueOnError { get { return true; } }

        public override void Process(EventPipelineContext ctx) {
            if (Settings.Current.WebsiteMode == WebsiteMode.Dev)
                return;

            // Throttle errors by client ip address to no more than X every 5 minutes.
            string clientIp = null;
            if (ctx.Event.RequestInfo != null && !String.IsNullOrEmpty(ctx.Event.RequestInfo.ClientIpAddress))
                clientIp = ctx.Event.RequestInfo.ClientIpAddress;

            if (String.IsNullOrEmpty(clientIp))
                return;

            string throttleCacheKey = String.Concat("bot:", clientIp, ":", DateTime.Now.Floor(_throttlingPeriod).Ticks);
            var requestCount = _cacheClient.Get<int?>(throttleCacheKey);
            if (requestCount != null) {
                _cacheClient.Increment(throttleCacheKey, 1);
                requestCount++;
            } else {
                _cacheClient.Set(throttleCacheKey, 1, _throttlingPeriod);
                requestCount = 1;
            }

            if (requestCount < Settings.Current.BotThrottleLimit)
                return;

            Log.Info().Message("Bot throttle triggered. IP: {0} Time: {1} Project: {2}", clientIp, DateTime.Now.Floor(_throttlingPeriod), ctx.Event.ProjectId).Project(ctx.Event.ProjectId).Write();
            // the throttle was triggered, go and delete all the errors that triggered the throttle to reduce bot noise in the system
            Task.Run(() => _eventRepository.RemoveAllByClientIpAndDateAsync(clientIp, DateTime.Now.Floor(_throttlingPeriod), DateTime.Now.Ceiling(_throttlingPeriod)));
            ctx.IsCancelled = true;
        }
    }
}