#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using Call = RecordingLiveNotifier.Call;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// The SaveChanges interceptors on the real, pooled plugin and items-planning
/// contexts (MariaDB): per watched row kind, a committed change yields its
/// hint after the request, a rolled-back one yields none; transactions,
/// coalescing, background flushes, and failures that must not reach the write.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveChangeInterceptorTests : TestBaseSetup
{
    private LiveChangeTestHost _host = null!;

    public sealed record Seed(int PropertyId, int AreaId, int PlanningId, int EventId);

    [SetUp]
    public void SetUpHost() => _host = NewHost();

    [TearDown]
    public void TearDownHost() => _host.Dispose();

    private LiveChangeTestHost NewHost(Action<IServiceCollection>? configure = null, bool connected = true,
        IInterceptor[]? extraInterceptors = null) =>
        new(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), null, configure, connected, extraInterceptors);

    /// <summary>Area → Property → AreaRule → Planning → AreaRulePlanning, through the fixture's (not intercepted) contexts.</summary>
    private async Task<Seed> SeedEventAsync()
    {
        var bc = BackendConfigurationPnDbContext!;
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(bc);
        var property = new Property
        {
            Name = $"Live-{Guid.NewGuid():N}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(bc);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(bc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Day, StartDate = DateTime.UtcNow.Date,
            RelatedEFormId = 7, Description = "Task",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id, ItemPlanningId = planning.Id,
            StartDate = DateTime.UtcNow.Date, Status = true, RepeatType = 1, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(bc);
        return new Seed(property.Id, area.Id, planning.Id, arp.Id);
    }

    private static Compliance NewCompliance(Seed s, int dayOffset = 1) => new()
    {
        PlanningId = s.PlanningId, PropertyId = s.PropertyId, AreaId = s.AreaId,
        Deadline = DateTime.UtcNow.Date.AddDays(dayOffset), StartDate = DateTime.UtcNow.Date,
        WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
    };

    private static BackendConfigurationPnDbContext Bc(IServiceProvider sp) => sp.GetRequiredService<BackendConfigurationPnDbContext>();
    private static ItemsPlanningPnDbContext Ip(IServiceProvider sp) => sp.GetRequiredService<ItemsPlanningPnDbContext>();

    /// <summary>One watched row kind: which context it is on, a write, and the hint it must produce.</summary>
    public sealed record Kind(
        string Name,
        bool OnItemsPlanning,
        Func<Seed, IServiceProvider, Task<int>> Write,
        Func<Seed, int, Call[]> Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly int Site = 424242;

    public static IEnumerable<object> Kinds() =>
    [
        new Kind("Compliance", false,
            async (s, sp) =>
            {
                var c = NewCompliance(s);
                await c.Create(Bc(sp));
                return c.Id;
            },
            (s, id) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, id, TaskChangeKinds.Changed)]),
        new Kind("AreaRulePlanning", false,
            async (s, sp) =>
            {
                var arp = await Bc(sp).AreaRulePlannings.SingleAsync(x => x.Id == s.EventId);
                arp.SendNotifications = !arp.SendNotifications;
                await arp.Update(Bc(sp));
                return arp.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        new Kind("PlanningSite (plugin)", false,
            async (s, sp) =>
            {
                var ps = new BcPlanningSite
                {
                    AreaRulePlanningsId = s.EventId, SiteId = Site,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await ps.Create(Bc(sp));
                return ps.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        new Kind("PlanningSite (items-planning)", true,
            async (s, sp) =>
            {
                var ps = new IpPlanningSite
                {
                    PlanningId = s.PlanningId, SiteId = Site,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await ps.Create(Ip(sp));
                return ps.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        new Kind("PlanningCaseSite", true,
            async (s, sp) =>
            {
                var pc = new PlanningCase
                {
                    PlanningId = s.PlanningId, MicrotingSdkSiteId = Site,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await pc.Create(Ip(sp));
                var pcs = new PlanningCaseSite
                {
                    PlanningId = s.PlanningId, PlanningCaseId = pc.Id, MicrotingSdkSiteId = Site,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await pcs.Create(Ip(sp));
                return pcs.Id;
            },
            // Two saves (case + case site) of one event: one hint.
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        new Kind("PropertyWorker", false,
            async (s, sp) =>
            {
                var pw = new PropertyWorker
                {
                    PropertyId = s.PropertyId, WorkerId = Site,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await pw.Create(Bc(sp));
                return pw.Id;
            },
            (_, _) => [new Call("AssignmentsChanged", SdkSiteId: Site)]),
        new Kind("CalendarOccurrenceException", false,
            async (s, sp) =>
            {
                var ex = new CalendarOccurrenceException
                {
                    AreaRulePlanningId = s.EventId, OriginalDate = DateTime.UtcNow.Date.AddDays(2),
                    NewDate = DateTime.UtcNow.Date.AddDays(3),
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await ex.Create(Bc(sp));
                return ex.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        // PR #1525 review: rows the list reads that touch no other watched row.
        new Kind("CalendarConfiguration", false,
            async (s, sp) =>
            {
                var cc = new CalendarConfiguration
                {
                    AreaRulePlanningId = s.EventId, StartHour = 10, Duration = 2,
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await cc.Create(Bc(sp));
                return cc.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        new Kind("AreaRulePlanningFile", false,
            async (s, sp) =>
            {
                var f = new AreaRulePlanningFile
                {
                    AreaRulePlanningId = s.EventId, UploadedDataId = 1, OriginalFileName = "a.pdf", MimeType = "application/pdf",
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await f.Create(Bc(sp));
                return f.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, s.EventId, null, null, TaskChangeKinds.Changed)]),
        new Kind("CalendarBoard", false,
            async (s, sp) =>
            {
                // A NEW board shows on no task (GetBoards creates a default one on read):
                // only its edit is a change.
                var b = new CalendarBoard
                {
                    PropertyId = s.PropertyId, Name = "Board", Color = "#fff",
                    WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
                };
                await b.Create(Bc(sp));
                b.Color = "#000";
                await b.Update(Bc(sp));
                return b.Id;
            },
            (s, _) => [new Call("TaskChanged", s.PropertyId, null, null, null, TaskChangeKinds.Property)]),
    ];

    [TestCaseSource(nameof(Kinds))]
    public async Task CommittedChange_YieldsItsHint_AfterTheRequest(Kind kind)
    {
        var seed = await SeedEventAsync();
        var id = 0;

        var calls = await _host.InRequestAsync(async sp => id = await kind.Write(seed, sp));

        Assert.That(calls, Is.EqualTo(kind.Expected(seed, id)));
    }

    [TestCaseSource(nameof(Kinds))]
    public async Task RolledBackChange_YieldsNothing(Kind kind)
    {
        var seed = await SeedEventAsync();

        var calls = await _host.InRequestAsync(async sp =>
        {
            DbContext db = kind.OnItemsPlanning ? Ip(sp) : Bc(sp);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await kind.Write(seed, sp);
                await tx.RollbackAsync();
            });
            db.ChangeTracker.Clear();
        });

        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task ATransactionThatIsOnlyDisposed_YieldsNothing()
    {
        var seed = await SeedEventAsync();

        var calls = await _host.InRequestAsync(async sp =>
        {
            var db = Bc(sp);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await NewCompliance(seed).Create(db);
            });
            db.ChangeTracker.Clear();
        });

        Assert.That(calls, Is.Empty);
        Assert.That(await BackendConfigurationPnDbContext!.Compliances.AnyAsync(x => x.PlanningId == seed.PlanningId), Is.False);
    }

    [Test]
    public async Task WithoutARequest_ASaveInATransaction_IsAnnouncedOnCommit_NotBefore()
    {
        var seed = await SeedEventAsync();
        var compliance = NewCompliance(seed);

        var calls = await _host.InBackgroundAsync(async sp =>
        {
            var db = Bc(sp);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await compliance.Create(db);
                await _host.Publisher.WhenIdleAsync();
                Assert.That(_host.Notifier.Calls, Is.Empty, "nothing before the commit");
                await tx.CommitAsync();
            });
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, compliance.Id, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task WithoutARequestOrBatch_EachSaveIsAnnouncedRightAfterIt()
    {
        var seed = await SeedEventAsync();

        await _host.InBackgroundAsync(async sp =>
        {
            await NewCompliance(seed).Create(Bc(sp));
            await _host.Publisher.WhenIdleAsync();
            Assert.That(_host.Notifier.Calls, Has.Count.EqualTo(1), "published after the save, before the scope ends");
        });
    }

    [Test]
    public async Task ABackgroundBatch_IsOneFlush_WhenItEnds()
    {
        var seed = await SeedEventAsync();
        var first = NewCompliance(seed, 1);
        var second = NewCompliance(seed, 2);

        var calls = await _host.InBackgroundAsync(async sp =>
        {
            using (LiveChangeScope.Begin(sp))
            {
                await first.Create(Bc(sp));
                await second.Create(Bc(sp));
                await _host.Publisher.WhenIdleAsync();
                Assert.That(_host.Notifier.Calls, Is.Empty, "nothing before the batch ends");
            }

            await _host.Publisher.WhenIdleAsync();
            Assert.That(_host.Notifier.Calls, Has.Count.EqualTo(1), "sent when the batch ends, before the scope does");
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task DeletingATasksCalendarConfiguration_IsAChangeOfTheTask()
    {
        var seed = await SeedEventAsync();
        var cc = new CalendarConfiguration
        {
            AreaRulePlanningId = seed.EventId, StartHour = 10, Duration = 2,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await cc.Create(BackendConfigurationPnDbContext!);

        var calls = await _host.InRequestAsync(async sp =>
        {
            var row = await Bc(sp).CalendarConfigurations.SingleAsync(x => x.Id == cc.Id);
            await row.Delete(Bc(sp));
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, null, TaskChangeKinds.Changed)
        }), "the task still exists; its timing changed");
    }

    [Test]
    public async Task ABackgroundTransaction_AppliesAllItsRevocations_BeforeDispatchingAnyHint()
    {
        // PR #1525 review: save 1 changes a task, save 2 unassigns a worker, one commit.
        // Every revocation of the transaction must run before the first hint is dispatched.
        var seed = await SeedEventAsync();
        var worker = new PropertyWorker
        {
            PropertyId = seed.PropertyId, WorkerId = Site,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await worker.Create(BackendConfigurationPnDbContext!);
        _host.Publisher.Dispatching = _ => _host.Notifier.Mark("Dispatch");

        var calls = await _host.InBackgroundAsync(async sp =>
        {
            var db = Bc(sp);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var arp = await db.AreaRulePlannings.SingleAsync(x => x.Id == seed.EventId);
                arp.SendNotifications = !arp.SendNotifications;
                await arp.Update(db);
                var row = await db.PropertyWorkers.SingleAsync(x => x.Id == worker.Id);
                await row.Delete(db);
                await tx.CommitAsync();
            });
        });

        var methods = calls.Select(c => c.Method).ToList();
        Assert.That(methods, Does.Contain("Dispatch").And.Contain("RevokePropertyAccess"));
        Assert.That(methods.IndexOf("RevokePropertyAccess"), Is.LessThan(methods.IndexOf("Dispatch")),
            string.Join(", ", methods));
    }

    [Test]
    public async Task SoftDeletingATask_IsRemoved()
    {
        var seed = await SeedEventAsync();

        var calls = await _host.InRequestAsync(async sp =>
        {
            var arp = await Bc(sp).AreaRulePlannings.SingleAsync(x => x.Id == seed.EventId);
            await arp.Delete(Bc(sp));
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, null, TaskChangeKinds.Removed)
        }));
    }

    [Test]
    public async Task UnassigningAWorker_RevokesAtCommit_ThenTellsItsSite_AndASecondDeleteIsSilent()
    {
        var seed = await SeedEventAsync();
        var worker = new PropertyWorker
        {
            PropertyId = seed.PropertyId, WorkerId = Site,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await worker.Create(BackendConfigurationPnDbContext!);

        var calls = await _host.InRequestAsync(async sp =>
        {
            var row = await Bc(sp).PropertyWorkers.SingleAsync(x => x.Id == worker.Id);
            await row.Delete(Bc(sp));
            Assert.That(_host.Notifier.Calls, Is.EqualTo(new[]
            {
                new Call("RevokePropertyAccess", PropertyId: seed.PropertyId, SdkSiteId: Site)
            }), "the revoke is not deferred to the end of the request");
            // AssignmentWorkerService.Delete removes the same rows twice.
            await row.Delete(Bc(sp));
            // An edit of another column is no assignment change.
            row.PinCode = "1234";
            await row.Update(Bc(sp));
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new Call("RevokePropertyAccess", PropertyId: seed.PropertyId, SdkSiteId: Site),
            new Call("AssignmentsChanged", SdkSiteId: Site)
        }));
    }

    [Test]
    public async Task ManyRowsOfOneEvent_AreOneHint_AndManyEvents_OnePropertyHint()
    {
        var seed = await SeedEventAsync();

        var oneEvent = await _host.InRequestAsync(async sp =>
        {
            for (var i = 1; i <= 3; i++) await NewCompliance(seed, i).Create(Bc(sp));
        });
        Assert.That(oneEvent, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, null, TaskChangeKinds.Changed)
        }));

        var others = new List<Seed>();
        for (var i = 0; i < LiveChangeLimits.PropertyWideThreshold; i++)
        {
            var arp = new AreaRulePlanning
            {
                AreaRuleId = (await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == seed.EventId)).AreaRuleId,
                PropertyId = seed.PropertyId, AreaId = seed.AreaId, ItemPlanningId = 0, Status = true,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await arp.Create(BackendConfigurationPnDbContext!);
            others.Add(seed with { EventId = arp.Id });
        }

        var manyEvents = await _host.InRequestAsync(async sp =>
        {
            foreach (var s in others.Append(seed))
            {
                var arp = await Bc(sp).AreaRulePlannings.SingleAsync(x => x.Id == s.EventId);
                arp.SendNotifications = !arp.SendNotifications;
                await arp.Update(Bc(sp));
            }
        });
        Assert.That(manyEvents, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, null, null, null, TaskChangeKinds.Property)
        }));
    }

    [Test]
    public async Task AFailingCollector_DoesNotFailTheWrite_AndIsLoggedOnce()
    {
        using var host = NewHost(s => s.AddSingleton<ILiveChangeTargets>(new ThrowingTargets()));
        var seed = await SeedEventAsync();

        await host.InRequestAsync(async sp =>
        {
            await NewCompliance(seed, 1).Create(Bc(sp));
            await NewCompliance(seed, 2).Create(Bc(sp));
        });

        Assert.That(await BackendConfigurationPnDbContext!.Compliances.CountAsync(x => x.PlanningId == seed.PlanningId),
            Is.EqualTo(2), "both writes saved");
        Assert.That(host.Warnings.WarningsOf<LiveChangeInterceptor>(), Is.EqualTo(1), "logged once, not per save");
        Assert.That(host.Notifier.Calls, Is.Empty);
    }

    [Test]
    public async Task AThrowingNotifier_DoesNotFailTheWrite_AndIsLoggedOnce()
    {
        var seed = await SeedEventAsync();
        _host.Notifier.Throw = new InvalidOperationException("hub down");
        var worker = new PropertyWorker
        {
            PropertyId = seed.PropertyId, WorkerId = Site,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };

        for (var i = 1; i <= 2; i++)
        {
            await _host.InRequestAsync(async sp => await NewCompliance(seed, i).Create(Bc(sp)));
        }

        await _host.InRequestAsync(async sp =>
        {
            await worker.Create(Bc(sp));
            await worker.Delete(Bc(sp)); // the revoke throws inside SaveChanges' commit step
        });

        Assert.That(await BackendConfigurationPnDbContext!.Compliances.CountAsync(x => x.PlanningId == seed.PlanningId),
            Is.EqualTo(2));
        Assert.That(await BackendConfigurationPnDbContext.PropertyWorkers.AsNoTracking().SingleAsync(x => x.Id == worker.Id),
            Has.Property(nameof(PropertyWorker.WorkflowState)).EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(_host.Warnings.WarningsOf<LiveChangePublisher>(), Is.EqualTo(2), "once for the sends, once for the revoke");
    }

    // ------------------------------------------------------------------
    // Review round 1
    // ------------------------------------------------------------------

    [Test]
    public async Task WithNoConnection_ASaveCapturesNothing_AndNothingIsLookedUp()
    {
        var targets = new CountingTargets();
        var resolver = Substitute.For<ILiveChangeResolver>();
        using var host = NewHost(s =>
        {
            s.AddSingleton<ILiveChangeTargets>(targets);
            s.AddSingleton(resolver);
        }, connected: false);
        var seed = await SeedEventAsync();

        var calls = await host.InRequestAsync(async sp =>
        {
            await NewCompliance(seed).Create(Bc(sp));
            var pw = new PropertyWorker
            {
                PropertyId = seed.PropertyId, WorkerId = Site,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await pw.Create(Bc(sp));
            await pw.Delete(Bc(sp));
            sp.GetRequiredService<ILiveChanges>().TaskChanged(seed.PropertyId, seed.EventId);
        });

        Assert.That(targets.Calls, Is.Zero, "no snapshot is taken");
        await resolver.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task AnUnassignmentStartedWithNobodyConnected_StillRevokes_AConnectionThatJoinedBeforeTheCommit()
    {
        // Review round 2: revocations are captured even with no audience; only hints need one.
        using var host = NewHost(connected: false);
        var seed = await SeedEventAsync();
        var worker = new PropertyWorker
        {
            PropertyId = seed.PropertyId, WorkerId = Site,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await worker.Create(BackendConfigurationPnDbContext!);

        var calls = await host.InBackgroundAsync(async sp =>
        {
            var db = Bc(sp);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var row = await db.PropertyWorkers.SingleAsync(x => x.Id == worker.Id);
                await row.Delete(db); // saved while nobody is connected
                // W connects and joins P (its PropertyWorker row is not committed as removed yet).
                host.Registry.TryConnect("w", 2, DateTimeOffset.MaxValue, () => { }, 10);
                host.Registry.SetSite("w", Site);
                host.Registry.Joined("w", LiveHubContract.PropertyGroup(seed.PropertyId));
                await tx.CommitAsync();
            });
        });

        Assert.That(calls, Does.Contain(new Call("RevokePropertyAccess", PropertyId: seed.PropertyId, SdkSiteId: Site)));
    }

    [Test]
    public async Task ASave_RunsChangeDetectionOnce_TheOnlyPassBeingEfsOwn()
    {
        var detections = new DetectionCounter();
        using var host = NewHost();
        var seed = await SeedEventAsync();
        var compliance = NewCompliance(seed);

        var calls = await host.InRequestAsync(async sp =>
        {
            var db = Bc(sp);
            detections.Watch(db);
            db.Compliances.Add(compliance);
            detections.Reset();
            await db.SaveChangesAsync();
            Assert.That(detections.Count, Is.EqualTo(1), "EF's own pass only");
        });

        Assert.That(calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, compliance.Id, TaskChangeKinds.Changed)
        }), "still captured, from EF's own pass");
    }

    [Test]
    public async Task WithAutoDetectOff_TrackedStatesAreStillCaptured_WithoutForcingADetection()
    {
        var detections = new DetectionCounter();
        using var host = NewHost();
        var seed = await SeedEventAsync();
        var compliance = NewCompliance(seed);

        var calls = await host.InRequestAsync(async sp =>
        {
            var db = Bc(sp);
            detections.Watch(db);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                db.Compliances.Add(compliance);
                detections.Reset();
                await db.SaveChangesAsync();
                Assert.That(detections.Count, Is.Zero, "no detection forced");
            }
            finally
            {
                db.ChangeTracker.AutoDetectChangesEnabled = true;
            }
        });

        Assert.That(calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TwoRequestsOnTheSamePooledContext_EachGetOnlyTheirOwnChanges()
    {
        var seed = await SeedEventAsync();
        var other = await SeedEventAsync();
        BackendConfigurationPnDbContext? first = null, second = null;

        var one = await _host.InRequestAsync(async sp =>
        {
            first = Bc(sp);
            await NewCompliance(seed).Create(first);
            // An uncommitted transaction left behind on the pooled instance.
            await first.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await first.Database.BeginTransactionAsync();
                var arp = await first.AreaRulePlannings.SingleAsync(x => x.Id == other.EventId);
                arp.SendNotifications = !arp.SendNotifications;
                await arp.Update(first);
            });
            first.ChangeTracker.Clear();
        });
        var two = await _host.InRequestAsync(async sp =>
        {
            second = Bc(sp);
            var arp = await second.AreaRulePlannings.SingleAsync(x => x.Id == seed.EventId);
            arp.SendNotifications = !arp.SendNotifications;
            await arp.Update(second);
        });

        Assert.That(second, Is.SameAs(first), "the pool handed the same instance to both requests");
        Assert.That(one.Select(c => (c.PropertyId, c.EventId)), Is.EqualTo(new[] { ((int?)seed.PropertyId, (int?)seed.EventId) }));
        Assert.That(two, Is.EqualTo(new[]
        {
            new Call("TaskChanged", seed.PropertyId, seed.EventId, null, null, TaskChangeKinds.Changed)
        }), "nothing of the first request, nor of its rolled-back transaction, leaks in");
    }

    [Test]
    public async Task TheResolveQueries_UseTheKeyAndThePropertyIndex_NeverAnOr()
    {
        var commands = new CommandRecorder();
        using var host = NewHost(extraInterceptors: [commands]);
        var seed = await SeedEventAsync();

        // A Compliance (planning + property) and a plugin PlanningSite (event only).
        await host.InRequestAsync(async sp =>
        {
            await NewCompliance(seed).Create(Bc(sp));
            await new BcPlanningSite
            {
                AreaRulePlanningsId = seed.EventId, SiteId = Site,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            }.Create(Bc(sp));
            commands.Clear();
        });
        // The WHERE clauses only (both queries select the same columns; one id is "=" not "IN").
        var resolves = commands.Selects("`AreaRulePlannings`")
            .Select(q => q[(q.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) + 5)..]).ToList();

        Assert.That(resolves, Has.Count.EqualTo(2), string.Join("\n---\n", resolves));
        Assert.That(resolves, Has.None.Contains(" OR "));
        Assert.That(resolves.Count(q => q.Contains("`PropertyId`") && q.Contains("`ItemPlanningId`")), Is.EqualTo(1),
            "plannings of a known property go through the PropertyId index: " + string.Join(" | ", resolves));
        Assert.That(resolves.Count(q => !q.Contains("`ItemPlanningId`") && q.Contains("`Id`")), Is.EqualTo(1),
            "events by primary key: " + string.Join(" | ", resolves));
    }

    private sealed class CountingTargets : ILiveChangeTargets
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public LiveChangeCollector? For(DbContext context)
        {
            Interlocked.Increment(ref _calls);
            return null;
        }
    }

    /// <summary>Counts full change-detection passes of one context (EF's ChangeTracker.DetectingAllChanges event).</summary>
    private sealed class DetectionCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);
        public void Watch(DbContext context) => context.ChangeTracker.DetectingAllChanges += (_, _) => Interlocked.Increment(ref _count);
    }

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _texts = new();
        public void Clear() => _texts.Clear();

        public List<string> Selects(string table) =>
            _texts.Where(t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && t.Contains(table)).ToList();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _texts.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ThrowingTargets : ILiveChangeTargets
    {
        public LiveChangeCollector For(DbContext context) => throw new InvalidOperationException("collector broken");
    }
}
