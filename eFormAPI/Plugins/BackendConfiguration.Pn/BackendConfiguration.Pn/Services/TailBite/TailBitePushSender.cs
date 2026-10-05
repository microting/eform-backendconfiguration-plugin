/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Sentry;

public interface ITailBitePushSender
{
    Task SendToSiteAsync(int targetSdkSiteId, string title, string body, Dictionary<string, string> data);
}

/// <summary>
/// FCM sender for the halebid app. It follows PushNotificationService's selection, send and prune logic, bound to the
/// halebid Firebase project instead; see that class for why each part is shaped the way it is (named app, credential
/// read per instance, systemic-fault prune guard). It has its own named app so SENDER_ID_MISMATCH from one Firebase
/// project never reaches the other sender's prune decision.
/// </summary>
public class TailBitePushSender : ITailBitePushSender
{
    // Client-side counterpart: the app_id the halebid app passes to SettingsGrpcService.RegisterPushToken (sub-project 2).
    public const string HalebidAppId = "halebid";

    // Named, never DefaultInstance: several plugins share one host process, each with its own Firebase project.
    private const string FirebaseAppName = "microting-halebid";

    private const string ServiceAccountConfigurationKey = "BackendConfigurationSettings:HalebidFirebaseServiceAccountJson";

    private static readonly object FirebaseInitLock = new();

    private readonly BackendConfigurationPnDbContext _dbContext;
    private readonly ILogger<TailBitePushSender> _logger;
    private readonly object _resolveLock = new();
    private bool _resolved;
    private FirebaseApp? _firebaseApp;

    public TailBitePushSender(BackendConfigurationPnDbContext dbContext, ILogger<TailBitePushSender> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // INVARIANT (as in PushNotificationService): always carries the AppId equality predicate, the leading column of
    // IX_DeviceTokens_AppId_SdkSiteId_WorkflowState.
    public static IQueryable<DeviceToken> TargetTokenQuery(IQueryable<DeviceToken> deviceTokens, int targetSdkSiteId) =>
        deviceTokens.Where(dt => dt.AppId == HalebidAppId
                                 && dt.SdkSiteId == targetSdkSiteId
                                 && dt.WorkflowState == Constants.WorkflowStates.Created);

    public async Task SendToSiteAsync(int targetSdkSiteId, string title, string body, Dictionary<string, string> data)
    {
        var firebaseApp = GetFirebaseApp();
        if (firebaseApp == null)
        {
            _logger.LogInformation(
                "Push notification skipped (halebid Firebase push disabled, {ConfigurationKey} not set): "
                + "SdkSiteId={SdkSiteId}, Title={Title}",
                ServiceAccountConfigurationKey, targetSdkSiteId, title);
            return;
        }

        try
        {
            var messaging = FirebaseMessaging.GetMessaging(firebaseApp);
            await SendAndPruneAsync(
                targetSdkSiteId,
                device => messaging.SendAsync(
                    global::BackendConfiguration.Pn.Services.PushNotificationService.PushNotificationService
                        .BuildMessage(device.FcmToken, title, body, data)));
        }
        catch (Exception ex)
        {
            // A push never fails the request that triggered it.
            _logger.LogError(ex, "Error sending halebid push notifications to SdkSiteId {SdkSiteId}", targetSdkSiteId);
        }
    }

    // Resolved once, on first use: the constructor must not touch the database or Firebase. ResolveFirebaseApp never throws.
    private FirebaseApp? GetFirebaseApp()
    {
        lock (_resolveLock)
        {
            if (!_resolved)
            {
                _firebaseApp = ResolveFirebaseApp(_dbContext, _logger);
                _resolved = true;
            }

            return _firebaseApp;
        }
    }

    private static FirebaseApp? ResolveFirebaseApp(BackendConfigurationPnDbContext dbContext, ILogger<TailBitePushSender> logger)
    {
        try
        {
            var serviceAccountJson = dbContext.PluginConfigurationValues
                .FirstOrDefault(x => x.Name == ServiceAccountConfigurationKey)?.Value;
            if (string.IsNullOrWhiteSpace(serviceAccountJson))
            {
                return null;
            }

            var app = EnsureFirebaseApp(serviceAccountJson);
            logger.LogInformation("Firebase push notifications initialized on app {FirebaseAppName}", FirebaseAppName);
            return app;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Firebase Admin SDK from {ConfigurationKey}", ServiceAccountConfigurationKey);
            return null;
        }
    }

    private static FirebaseApp EnsureFirebaseApp(string serviceAccountJson)
    {
        var app = FirebaseApp.GetInstance(FirebaseAppName);
        if (app != null)
        {
            return app;
        }

        lock (FirebaseInitLock)
        {
            var existing = FirebaseApp.GetInstance(FirebaseAppName);
            if (existing != null)
            {
                return existing;
            }

            try
            {
                return FirebaseApp.Create(
                    new AppOptions
                    {
                        Credential = CredentialFactory
                            .FromJson<ServiceAccountCredential>(serviceAccountJson)
                            .ToGoogleCredential()
                    },
                    FirebaseAppName);
            }
            catch (ArgumentException)
            {
                var raced = FirebaseApp.GetInstance(FirebaseAppName);
                if (raced == null)
                {
                    throw;
                }

                return raced;
            }
        }
    }

    internal async Task SendAndPruneAsync(int targetSdkSiteId, Func<DeviceToken, Task> sendOne)
    {
        var devices = await TargetTokenQuery(_dbContext.DeviceTokens, targetSdkSiteId).ToListAsync();
        if (devices.Count == 0)
        {
            _logger.LogInformation("No halebid devices registered for SdkSiteId {SdkSiteId}", targetSdkSiteId);
            return;
        }

        var permanentFailures = new List<(DeviceToken Device, MessagingErrorCode Code)>();
        var respondedCount = 0;
        foreach (var device in devices)
        {
            try
            {
                await sendOne(device);
                respondedCount++;
            }
            catch (FirebaseMessagingException fex)
                when (fex.MessagingErrorCode is MessagingErrorCode.SenderIdMismatch
                      or MessagingErrorCode.Unregistered
                      or MessagingErrorCode.InvalidArgument)
            {
                respondedCount++;
                permanentFailures.Add((device, fex.MessagingErrorCode!.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send halebid push notification to device {DeviceId} for SdkSiteId {SdkSiteId}",
                    device.Id, targetSdkSiteId);
            }
        }

        await PrunePermanentFailuresAsync(permanentFailures, respondedCount, targetSdkSiteId);
    }

    private static bool CanBeSystemic(MessagingErrorCode code) =>
        code is MessagingErrorCode.SenderIdMismatch or MessagingErrorCode.InvalidArgument;

    private async Task PrunePermanentFailuresAsync(
        IReadOnlyList<(DeviceToken Device, MessagingErrorCode Code)> permanentFailures, int respondedCount, int targetSdkSiteId)
    {
        if (permanentFailures.Count == 0)
        {
            return;
        }

        var firstCode = permanentFailures[0].Code;
        if (permanentFailures.Count == respondedCount
            && CanBeSystemic(firstCode)
            && permanentFailures.All(f => f.Code == firstCode))
        {
            // Every answered device failed the same systemic way: a fault in this sender, not the devices. Keep them.
            _logger.LogWarning(
                "All {Count} halebid devices FCM answered for on SdkSiteId {SdkSiteId} failed with {Error} - keeping them. "
                + "SenderIdMismatch means {ConfigurationKey} holds the wrong Firebase project's credential; fix it and RESTART "
                + "the host (the Firebase app is cached process-wide). InvalidArgument means the payload is malformed",
                respondedCount, targetSdkSiteId, firstCode, ServiceAccountConfigurationKey);
            SentrySdk.CaptureMessage(
                $"All {respondedCount} halebid devices answered for on SdkSiteId {targetSdkSiteId} failed with "
                + $"{firstCode} - treated as a fault in this sender, none pruned. Check {ServiceAccountConfigurationKey} "
                + "(then restart the host) or the message payload",
                SentryLevel.Warning);
            return;
        }

        foreach (var (device, code) in permanentFailures)
        {
            // Per device: a failing delete must not strand the remaining dead rows.
            try
            {
                if (code == MessagingErrorCode.SenderIdMismatch)
                {
                    _logger.LogWarning(
                        "Removing foreign halebid device {DeviceId} for SdkSiteId {SdkSiteId}: SenderIdMismatch",
                        device.Id, targetSdkSiteId);
                    SentrySdk.CaptureMessage(
                        $"SenderIdMismatch for halebid device {device.Id} (SdkSiteId {targetSdkSiteId})",
                        SentryLevel.Warning);
                }
                else
                {
                    _logger.LogInformation("Removing stale halebid device {DeviceId} for SdkSiteId {SdkSiteId}: {Error}",
                        device.Id, targetSdkSiteId, code);
                }

                await device.Delete(_dbContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prune halebid device {DeviceId} for SdkSiteId {SdkSiteId} after {Error}",
                    device.Id, targetSdkSiteId, code);
            }
        }
    }
}
