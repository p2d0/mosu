using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Statistics;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;
using Realms;
using Realms.Exceptions;
using FileInfo = System.IO.FileInfo;

namespace osu.Game.Rulesets.MOsu.Database
{
    /// <summary>
    /// A factory which provides safe access to the MOsu realm storage backend.
    /// </summary>
    public class MOsuRealmAccess : IDisposable
    {
        private readonly Storage storage;

        /// <summary>
        /// The filename of this realm.
        /// </summary>
        public readonly string Filename;

        private readonly SynchronizationContext? updateThreadSyncContext;

        /// <summary>
        /// Version history:
        /// 1    Initial MOsu Realm setup.
        /// 2    2025-05-26  Added PresetImportState.
        /// </summary>
        private const int schema_version = 4;

        /// <summary>
        /// Lock object which is held during <see cref="BlockAllOperations"/> sections, blocking realm retrieval during blocking periods.
        /// </summary>
        private readonly SemaphoreSlim realmRetrievalLock = new SemaphoreSlim(1);

        private readonly CountdownEvent pendingAsyncOperations = new CountdownEvent(0);

        /// <summary>
        /// <c>true</c> when the current thread has already entered the <see cref="realmRetrievalLock"/>.
        /// </summary>
        private readonly ThreadLocal<bool> currentThreadHasRealmRetrievalLock = new ThreadLocal<bool>();

        private readonly Dictionary<Func<Realm, IDisposable?>, IDisposable?> customSubscriptionsResetMap = new Dictionary<Func<Realm, IDisposable?>, IDisposable?>();

        private readonly Dictionary<Func<Realm, IDisposable?>, Action> notificationsResetMap = new Dictionary<Func<Realm, IDisposable?>, Action>();

        // Renamed statistics to avoid conflict with the main game Realm
        private static readonly GlobalStatistic<int> realm_instances_created = GlobalStatistics.Get<int>(@"MOsuRealm", @"Instances (Created)");
        private static readonly GlobalStatistic<int> total_subscriptions = GlobalStatistics.Get<int>(@"MOsuRealm", @"Subscriptions");
        private static readonly GlobalStatistic<int> total_reads_update = GlobalStatistics.Get<int>(@"MOsuRealm", @"Reads (Update)");
        private static readonly GlobalStatistic<int> total_reads_async = GlobalStatistics.Get<int>(@"MOsuRealm", @"Reads (Async)");
        private static readonly GlobalStatistic<int> total_writes_update = GlobalStatistics.Get<int>(@"MOsuRealm", @"Writes (Update)");
        private static readonly GlobalStatistic<int> total_writes_async = GlobalStatistics.Get<int>(@"MOsuRealm", @"Writes (Async)");

        private Realm? updateRealm;

        private bool hasInitialisedOnce;
        private bool isSendingNotificationResetEvents;

        public Realm Realm => ensureUpdateRealm();

        private const string realm_extension = @".realm";

        // Default to "mosurealm"
        private const string default_filename = @"mosurealm";

        private Realm ensureUpdateRealm()
        {
            if (isSendingNotificationResetEvents)
                throw new InvalidOperationException("Cannot retrieve a realm context from a notification callback during a blocking operation.");

            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException(@$"Use {nameof(getRealmInstance)} when performing realm operations from a non-update thread");

            if (updateRealm == null)
            {
                updateRealm = getRealmInstance();
                hasInitialisedOnce = true;

                Logger.Log(@$"Opened MOsu realm ""{updateRealm.Config.DatabasePath}"" at version {updateRealm.Config.SchemaVersion}");

                foreach (var action in customSubscriptionsResetMap.Keys.ToArray())
                    registerSubscription(action);
            }

            Debug.Assert(updateRealm != null);

            return updateRealm;
        }

        internal static bool CurrentThreadSubscriptionsAllowed => current_thread_subscriptions_allowed.Value;

        private static readonly ThreadLocal<bool> current_thread_subscriptions_allowed = new ThreadLocal<bool>();

        /// <summary>
        /// Construct a new instance.
        /// </summary>
        /// <param name="storage">The game storage which will be used to create the realm backing file.</param>
        /// <param name="filename">The filename to use. Defaults to "mosurealm".</param>
        /// <param name="updateThread">The game update thread.</param>
        public MOsuRealmAccess(Storage storage, string filename = default_filename, GameThread? updateThread = null)
        {
            this.storage = storage;

            updateThreadSyncContext = updateThread?.SynchronizationContext ?? SynchronizationContext.Current;

            Filename = filename;

            if (!Filename.EndsWith(realm_extension, StringComparison.Ordinal))
                Filename += realm_extension;

#if DEBUG
            if (!DebugUtils.IsNUnitRunning)
                applyFilenameSchemaSuffix(ref Filename);
#endif

            using (var realm = prepareFirstRealmAccess())
                cleanupPendingDeletions(realm);
        }

        private void applyFilenameSchemaSuffix(ref string filename)
        {
            string originalFilename = filename;

            filename = getVersionedFilename(schema_version);

            if (storage.Exists(filename))
                return;

            for (int i = schema_version - 1; i >= 0; i--)
            {
                string previousFilename = getVersionedFilename(i);

                if (storage.Exists(previousFilename))
                {
                    copyPreviousVersion(previousFilename, filename);
                    return;
                }
            }

            if (storage.Exists(originalFilename))
                copyPreviousVersion(originalFilename, filename);

            void copyPreviousVersion(string previousFilename, string newFilename)
            {
                using (var previous = storage.GetStream(previousFilename))
                using (var current = storage.CreateFileSafely(newFilename))
                {
                    Logger.Log(@$"Copying previous MOsu realm database {previousFilename} to {newFilename} for migration to schema version {schema_version}");
                    previous.CopyTo(current);
                }
            }

            string getVersionedFilename(int version) => originalFilename.Replace(realm_extension, $"_{version}{realm_extension}");
        }

        private void attemptRecoverFromFile(string recoveryFilename)
        {
            Logger.Log($@"Performing recovery from {recoveryFilename}", LoggingTarget.Database);

            try
            {
                // NOTE: If you have a specific Main Model (like ScoreInfo in standard osu), check for it here.
                // using (var realm = Realm.GetInstance(getConfiguration()))
                // {
                //     if (realm.All<SomeMainModel>().Any()) { ... abort ... }
                // }
            }
            catch
            {
            }

            try
            {
                using (Realm.GetInstance(getConfiguration(recoveryFilename)))
                {
                }
            }
            catch
            {
                Logger.Log(@"Recovery aborted as the newer version could not be loaded by this version.", LoggingTarget.Database);
                return;
            }

            createBackup($"{Filename.Replace(realm_extension, string.Empty)}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_newer_version_before_recovery{realm_extension}");

            storage.Delete(Filename);

            using (var inputStream = storage.GetStream(recoveryFilename))
            using (var outputStream = storage.CreateFileSafely(Filename))
                inputStream.CopyTo(outputStream);

            storage.Delete(recoveryFilename);
            Logger.Log(@"Recovery complete!", LoggingTarget.Database);
        }

        private Realm prepareFirstRealmAccess()
        {
            string newerVersionFilename = $"{Filename.Replace(realm_extension, string.Empty)}_newer_version{realm_extension}";

            if (storage.Exists(newerVersionFilename))
            {
                Logger.Log(@"A newer MOsu realm database has been found, attempting recovery...", LoggingTarget.Database);
                attemptRecoverFromFile(newerVersionFilename);
            }

            try
            {
                string fullPath = storage.GetFullPath(Filename);
                var fi = new FileInfo(fullPath);
                if (fi.Exists)
                    fi.LastWriteTime = DateTime.Now;
            }
            catch { }

            try
            {
                return getRealmInstance();
            }
            catch (Exception e)
            {
                if (e.Message.StartsWith(@"Provided schema version", StringComparison.Ordinal))
                {
                    Logger.Error(e, "Your local MOsu database is too new to work with this version.");

                    if (!storage.Exists(newerVersionFilename))
                        createBackup(newerVersionFilename);
                }
                else
                {
                    if (e.Message.StartsWith("SetEndOfFile() failed", StringComparison.Ordinal))
                    {
                        FileUtils.AttemptOperation(() =>
                        {
                            if (storage.Exists(Filename))
                            {
                                using (var _ = storage.GetStream(Filename, FileAccess.ReadWrite))
                                {
                                }
                            }
                        }, 20);
                        return getRealmInstance();
                    }

                    Logger.Error(e, "MOsu Realm startup failed with unrecoverable error; starting with a fresh database. A backup has been made.");
                    createBackup($"{Filename.Replace(realm_extension, string.Empty)}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_corrupt{realm_extension}");
                }

                storage.Delete(Filename);
                return getRealmInstance();
            }
        }

        private void cleanupPendingDeletions(Realm realm)
        {
            try
            {
                using (var transaction = realm.BeginWrite())
                {
                    // TODO: Implement your specific model deletion logic here if your models support soft-deletion (DeletePending).
                    // Below is the example from the main game for reference:

                    /*
                    var pendingDeleteScores = realm.All<ScoreInfo>().Where(s => s.DeletePending);
                    foreach (var score in pendingDeleteScores)
                        realm.Remove(score);

                    var pendingDeleteSets = realm.All<BeatmapSetInfo>().Where(s => s.DeletePending);
                    foreach (var beatmapSet in pendingDeleteSets)
                    {
                        foreach (var beatmap in beatmapSet.Beatmaps)
                        {
                            realm.Remove(beatmap.Metadata);
                            realm.Remove(beatmap);
                        }
                        realm.Remove(beatmapSet);
                    }
                    */

                    transaction.Commit();
                }

                // Note: RealmFileStore depends on RealmAccess. If you need file storage for this specific DB,
                // you will need a separate FileStore implementation or adapter.
                // new RealmFileStore(this, storage).Cleanup();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to clean up unused files.");
            }
        }

        public bool Compact() => Realm.Compact(getConfiguration());

        public T Run<T>(Func<Realm, T> action)
        {
            if (ThreadSafety.IsUpdateThread)
            {
                total_reads_update.Value++;
                return action(Realm);
            }

            total_reads_async.Value++;
            using (var realm = getRealmInstance())
                return action(realm);
        }

        public void Run(Action<Realm> action)
        {
            if (ThreadSafety.IsUpdateThread)
            {
                total_reads_update.Value++;
                action(Realm);
            }
            else
            {
                total_reads_async.Value++;
                using (var realm = getRealmInstance())
                    action(realm);
            }
        }

        public Task<T> RunAsync<T>(Func<Realm, T> action, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException($@"{nameof(RunAsync)} must be called from the update thread.");

            if (!pendingAsyncOperations.TryAddCount())
                pendingAsyncOperations.Reset(1);

            return Task.Run(() =>
            {
                var result = Run(action);
                pendingAsyncOperations.Signal();
                return result;
            }, token);
        }

        public T Write<T>(Func<Realm, T> action)
        {
            if (ThreadSafety.IsUpdateThread)
            {
                total_writes_update.Value++;
                return Realm.Write(action);
            }
            else
            {
                total_writes_async.Value++;

                using (var realm = getRealmInstance())
                    return realm.Write(action);
            }
        }

        public void Write(Action<Realm> action)
        {
            if (ThreadSafety.IsUpdateThread)
            {
                total_writes_update.Value++;
                Realm.Write(action);
            }
            else
            {
                total_writes_async.Value++;

                using (var realm = getRealmInstance())
                    realm.Write(action);
            }
        }

        public Task WriteAsync(Action<Realm> action)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException(@$"{nameof(WriteAsync)} must be called from the update thread.");

            if (!pendingAsyncOperations.TryAddCount())
                pendingAsyncOperations.Reset(1);

            var writeTask = Task.Run(async () =>
            {
                total_writes_async.Value++;
                using (var realm = getRealmInstance())
                    await realm.WriteAsync(() => action(realm)).ConfigureAwait(false);

                pendingAsyncOperations.Signal();
            });

            return writeTask;
        }

        public Task<T> WriteAsync<T>(Func<Realm, T> action)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException(@$"{nameof(WriteAsync)} must be called from the update thread.");

            if (!pendingAsyncOperations.TryAddCount())
                pendingAsyncOperations.Reset(1);

            var writeTask = Task.Run(async () =>
            {
                T result;
                total_writes_async.Value++;
                using (var realm = getRealmInstance())
                    result = await realm.WriteAsync(() => action(realm)).ConfigureAwait(false);

                pendingAsyncOperations.Signal();
                return result;
            });

            return writeTask;
        }

        public IDisposable RegisterForNotifications<T>(Func<Realm, IQueryable<T>> query, NotificationCallbackDelegate<T> callback)
            where T : RealmObjectBase
        {
            Func<Realm, IDisposable?> action = realm => query(realm).QueryAsyncWithNotifications(callback);

            lock (notificationsResetMap)
            {
                notificationsResetMap.Add(action, () => callback(new RealmResetEmptySet<T>(), null));
            }

            return RegisterCustomSubscription(action);
        }

        public IDisposable SubscribeToPropertyChanged<TModel, TProperty>(Func<Realm, TModel?> modelAccessor, Expression<Func<TModel, TProperty>> propertyLookup, Action<TProperty> onChanged)
            where TModel : RealmObjectBase
        {
            return RegisterCustomSubscription(_ =>
            {
                string propertyName = getMemberName(propertyLookup);

                var model = Run(modelAccessor);
                var propLookupCompiled = propertyLookup.Compile();

                if (model == null)
                    return null;

                model.PropertyChanged += onPropertyChanged;
                onChanged(propLookupCompiled(model));

                return new InvokeOnDisposal(() => model.PropertyChanged -= onPropertyChanged);

                void onPropertyChanged(object? sender, PropertyChangedEventArgs args)
                {
                    if (args.PropertyName == propertyName)
                        onChanged(propLookupCompiled(model));
                }
            });

            static string getMemberName(Expression<Func<TModel, TProperty>> expression)
            {
                if (!(expression is LambdaExpression lambda))
                    throw new ArgumentException("Outermost expression must be a lambda expression", nameof(expression));

                if (!(lambda.Body is MemberExpression memberExpression))
                    throw new ArgumentException("Lambda body must be a member access expression", nameof(expression));

                if (memberExpression.Expression != lambda.Parameters[0])
                    throw new ArgumentException("Nested access expressions are not supported", nameof(expression));

                return memberExpression.Member.Name;
            }
        }

        public IDisposable RegisterCustomSubscription(Func<Realm, IDisposable?> action)
        {
            if (updateThreadSyncContext == null)
                throw new InvalidOperationException("Attempted to register a realm subscription before update thread registration.");

            total_subscriptions.Value++;

            if (ThreadSafety.IsUpdateThread)
                updateThreadSyncContext.Send(_ => registerSubscription(action), null);
            else
                updateThreadSyncContext.Post(_ => registerSubscription(action), null);

            return new InvokeOnDisposal(() =>
            {
                if (ThreadSafety.IsUpdateThread)
                    updateThreadSyncContext.Send(_ => unsubscribe(), null);
                else
                    updateThreadSyncContext.Post(_ => unsubscribe(), null);

                void unsubscribe()
                {
                    if (customSubscriptionsResetMap.TryGetValue(action, out var unsubscriptionAction))
                    {
                        unsubscriptionAction?.Dispose();
                        customSubscriptionsResetMap.Remove(action);

                        lock (notificationsResetMap)
                        {
                            notificationsResetMap.Remove(action);
                        }

                        total_subscriptions.Value--;
                    }
                }
            });
        }

        private void registerSubscription(Func<Realm, IDisposable?> action)
        {
            Debug.Assert(ThreadSafety.IsUpdateThread);

            var realm = Realm;

            Debug.Assert(!customSubscriptionsResetMap.TryGetValue(action, out var found) || found == null);

            current_thread_subscriptions_allowed.Value = true;
            customSubscriptionsResetMap[action] = action(realm);
            current_thread_subscriptions_allowed.Value = false;
        }

        private Realm getRealmInstance()
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            bool tookSemaphoreLock = false;

            try
            {
                if (!currentThreadHasRealmRetrievalLock.Value)
                {
                    realmRetrievalLock.Wait();
                    currentThreadHasRealmRetrievalLock.Value = true;
                    tookSemaphoreLock = true;
                }

                realm_instances_created.Value++;

                return Realm.GetInstance(getConfiguration());
            }
            finally
            {
                if (tookSemaphoreLock)
                {
                    realmRetrievalLock.Release();
                    currentThreadHasRealmRetrievalLock.Value = false;
                }
            }
        }

        private RealmConfiguration getConfiguration(string? filename = null)
        {
            string tempPathLocation = Path.Combine(Path.GetTempPath(), @"lazer_mosu");
            if (!Directory.Exists(tempPathLocation))
                Directory.CreateDirectory(tempPathLocation);

            return new RealmConfiguration(storage.GetFullPath(filename ?? Filename, true))
            {
                SchemaVersion = schema_version,
                MigrationCallback = onMigration,
                Schema = new[]
                {
                    typeof(BeatmapModPreset),
                    typeof(RulesetInfo),
                    typeof(osu.Game.Rulesets.MOsu.Models.PresetImportState),
                    typeof(osu.Game.Rulesets.MOsu.Models.LocalProfile),
                },
                FallbackPipePath = tempPathLocation,
            };
        }

        private void onMigration(Migration migration, ulong lastSchemaVersion)
        {
            for (ulong i = lastSchemaVersion + 1; i <= schema_version; i++)
                applyMigrationsForVersion(migration, i);
        }

        private void applyMigrationsForVersion(Migration migration, ulong targetVersion)
        {
            // Migrations for the MOsu database will go here.
            // Since this is a new DB file, we start fresh and don't include the legacy osu! migrations.
            switch (targetVersion)
            {
                // case 2: ...
                // break;
            }
        }

        private static string getMappedOrOriginalName(MemberInfo member) => member.GetCustomAttribute<MapToAttribute>()?.Mapping ?? member.Name;

        public void CreateBackup(string backupFilename)
        {
            if (realmRetrievalLock.CurrentCount != 0)
                throw new InvalidOperationException($"Call {nameof(BlockAllOperations)} before creating a backup.");

            createBackup(backupFilename);
        }

        private void createBackup(string backupFilename)
        {
            Logger.Log($"Creating MOsu realm database backup at {backupFilename}", LoggingTarget.Database);

            FileUtils.AttemptOperation(() =>
            {
                using (var source = storage.GetStream(Filename, mode: FileMode.Open))
                {
                    if (source == null)
                        return;

                    using (var destination = storage.GetStream(backupFilename, FileAccess.Write, FileMode.CreateNew))
                        source.CopyTo(destination);
                }
            }, 20);
        }

        public IDisposable BlockAllOperations(string reason)
        {
            Logger.Log($@"Attempting to block all MOsu realm operations for {reason}.", LoggingTarget.Database);

            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException(@$"{nameof(BlockAllOperations)} must be called from the update thread.");

            ObjectDisposedException.ThrowIf(isDisposed, this);

            SynchronizationContext? syncContext = null;

            try
            {
                realmRetrievalLock.Wait();

                if (hasInitialisedOnce)
                {
                    syncContext = SynchronizationContext.Current;

                    foreach (var action in customSubscriptionsResetMap.ToArray())
                    {
                        action.Value?.Dispose();
                        customSubscriptionsResetMap[action.Key] = null;
                    }

                    updateRealm?.Dispose();
                    updateRealm = null;
                }

                Logger.Log(@"Lock acquired for blocking operations", LoggingTarget.Database);

                const int sleep_length = 200;
                int timeSpent = 0;

                try
                {
                    while (!Compact())
                    {
                        Thread.Sleep(sleep_length);
                        timeSpent += sleep_length;

                        if (timeSpent > 5000)
                            throw new TimeoutException($@"Realm compact failed after {timeSpent / sleep_length} attempts over {timeSpent / 1000} seconds");
                    }
                }
                catch (RealmException e)
                {
                    Logger.Log($"Realm compact failed with error {e}", LoggingTarget.Database);
                }

                Logger.Log(@"Realm usage isolated via compact", LoggingTarget.Database);

                syncContext?.Send(_ =>
                {
                    isSendingNotificationResetEvents = true;

                    try
                    {
                        lock (notificationsResetMap)
                        {
                            foreach (var action in notificationsResetMap.Values)
                                action();
                        }
                    }
                    finally
                    {
                        isSendingNotificationResetEvents = false;
                    }
                }, null);
            }
            catch
            {
                restoreOperation();
                throw;
            }

            return new InvokeOnDisposal(restoreOperation);

            void restoreOperation()
            {
                Logger.Log(@"Restoring MOsu realm operations.", LoggingTarget.Database);
                realmRetrievalLock.Release();

                if (syncContext == null) return;

                ManualResetEventSlim updateRealmReestablished = new ManualResetEventSlim();

                if (ThreadSafety.IsUpdateThread)
                {
                    syncContext.Send(_ =>
                    {
                        ensureUpdateRealm();
                        updateRealmReestablished.Set();
                    }, null);
                }
                else
                {
                    syncContext.Post(_ =>
                    {
                        ensureUpdateRealm();
                        updateRealmReestablished.Set();
                    }, null);
                }

                if (!updateRealmReestablished.Wait(10000))
                    throw new TimeoutException(@"Reestablishing update realm after block took too long");
            }
        }

        private bool isDisposed;

        public void Dispose()
        {
            if (!pendingAsyncOperations.Wait(10000))
                Logger.Log("MOsu Realm took too long waiting on pending async writes", level: LogLevel.Error);

            updateRealm?.Dispose();

            if (!isDisposed)
            {
                realmRetrievalLock.Wait();
                realmRetrievalLock.Dispose();

                isDisposed = true;
            }
        }
    }
}
