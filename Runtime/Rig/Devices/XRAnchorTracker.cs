using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.SpatialEntities;
using WezzelNL.XRKit.Runtime.Data;
using XRAnchor = Data.XRAnchor;
using XRSubsystemAnchor = UnityEngine.XR.ARSubsystems.XRAnchor;

namespace WezzelNL.XRKit.Runtime.Rig.Devices
{
	public class XRAnchorTracker : XRDevice
	{
		[field: SerializeField] public string StoreFileName { get; private set; } = "XRKitAnchors.json";
		public event Action<XRAnchor> OnAnchorLoaded;
		public event Action<XRAnchor> OnAnchorSaved;
		private const string LogPrefix = "[XRAnchorTracker]";
		private const float StatusLogInterval = 5f;
		private SpatialAnchorSubsystem subsystem;
		private CancellationTokenSource lifetime;
		private AnchorStore store = new();
		private bool startedByTracker;
		private bool starting;
		private bool requestingPermission;
		private bool wasRunning;
		private float nextStatusLogTime;
		private readonly List<XRAnchor> anchors = new();
		private readonly List<XRAnchor> pendingAnchors = new();
		private readonly Dictionary<XRAnchor, AnchorRecord> records = new();
		private readonly Dictionary<TrackableId, XRAnchor> anchorsByTrackable = new();
		private string StorePath => Path.Combine(Application.persistentDataPath, StoreFileName);
		private static List<SpatialAnchorSubsystem> SubsystemBuffer { get; } = new();
		private static List<XRAnchorSubsystem> AnyAnchorSubsystemBuffer { get; } = new();
		private static string[] RequiredExtensions { get; } = { "XR_EXT_future", "XR_EXT_spatial_entity", "XR_EXT_spatial_anchor", "XR_EXT_spatial_persistence", "XR_EXT_spatial_persistence_operations" };

		protected override void OnEnable()
		{
			base.OnEnable();
			lifetime = new CancellationTokenSource();
			LogEnvironment();

			var permission = GetPermission();
			if (Permission.HasUserAuthorizedPermission(permission))
			{
				Debug.Log($"{LogPrefix} Permission '{permission}' already granted.");
				StartSubsystem(lifetime.Token);
				return;
			}

			RequestPermission(permission);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			lifetime?.Cancel();
			lifetime?.Dispose();
			lifetime = null;

			ReleaseAnchors();

			if (startedByTracker && subsystem is { running: true })
			{
				subsystem.Stop();
				Debug.Log($"{LogPrefix} Stopped anchor subsystem.");
			}
			startedByTracker = false;
			starting = false;
			wasRunning = false;
			subsystem = null;
		}

		private void Update()
		{
			LogStatus();

			if (subsystem is not { running: true })
			{
				if (wasRunning) Debug.LogError($"{LogPrefix} Anchor subsystem stopped running unexpectedly.");
				wasRunning = false;
				TrackingState = XRDeviceTrackingState.None;
				return;
			}

			wasRunning = true;
			TrackingState = XRDeviceTrackingState.Position | XRDeviceTrackingState.Rotation;

			using var changes = subsystem.GetChanges(Allocator.Temp);
			foreach (var runtimeAnchor in changes.updated) UpdateAnchorPose(runtimeAnchor);
			foreach (var trackableId in changes.removed) if (anchorsByTrackable.ContainsKey(trackableId)) Debug.LogWarning($"{LogPrefix} Runtime removed anchor {trackableId}. It stays saved and will load again next session.");
		}

		public XRAnchor CreateAnchor(Vector3 position)
		{
			var anchor = CreateAnchorObject("Spatial Anchor");
			anchor.Initialize(this, position);
			anchors.Add(anchor);
			records.Add(anchor, new AnchorRecord());
			Debug.Log($"{LogPrefix} Created anchor {anchor.ID} at {position}.");

			SchedulePersist(anchor);
			return anchor;
		}

		public void MoveAnchor(XRAnchor anchor, Vector3 position)
		{
			if (!anchor)
			{
				Debug.LogWarning($"{LogPrefix} MoveAnchor was called with a null or destroyed anchor.");
				return;
			}

			if (!records.TryGetValue(anchor, out var record))
			{
				Debug.LogWarning($"{LogPrefix} Anchor {anchor.ID} is not managed by this tracker.");
				return;
			}

			anchor.transform.position = position;
			record.Version++;
			if (record.TrackableId != TrackableId.invalidId) anchorsByTrackable.Remove(record.TrackableId);

			if (record.IsPersisting) Debug.Log($"{LogPrefix} Moved anchor {anchor.ID} to {position}. It will be recreated once the current save finishes.");
			else Debug.Log($"{LogPrefix} Moved anchor {anchor.ID} to {position}. Recreating stored anchor.");
			SchedulePersist(anchor);
		}

		public void DeleteAnchor(XRAnchor anchor)
		{
			if (!anchor)
			{
				Debug.LogWarning($"{LogPrefix} DeleteAnchor was called with a null or destroyed anchor.");
				return;
			}

			if (!records.Remove(anchor, out var record))
			{
				Debug.LogWarning($"{LogPrefix} Anchor {anchor.ID} is not managed by this tracker.");
				return;
			}

			record.IsDeleted = true;
			anchors.Remove(anchor);
			pendingAnchors.Remove(anchor);
			Debug.Log($"{LogPrefix} Deleting anchor {anchor.ID}.");

			if (record.TrackableId != TrackableId.invalidId)
			{
				anchorsByTrackable.Remove(record.TrackableId);
				if (subsystem is { running: true } && !subsystem.TryRemoveAnchor(record.TrackableId)) Debug.LogError($"{LogPrefix} TryRemoveAnchor failed for {record.TrackableId}.");
			}

			if (record.IsSaved) EraseSaved(record.SavedGuid);
			Destroy(anchor.gameObject);
		}

		public IEnumerable<XRAnchor> GetAnchors() => new List<XRAnchor>(anchors);

		private void LogEnvironment()
		{
			Debug.Log($"{LogPrefix} Device: '{SystemInfo.deviceModel}', OpenXR runtime: '{OpenXRRuntime.name}' {OpenXRRuntime.version}");

			foreach (var extension in RequiredExtensions)
			{
				if (OpenXRRuntime.IsExtensionEnabled(extension)) Debug.Log($"{LogPrefix} Extension '{extension}' is enabled.");
				else Debug.LogError($"{LogPrefix} Extension '{extension}' is NOT enabled. The runtime doesn't support it or the Spatial Entities feature didn't request it.");
			}

			var settings = OpenXRSettings.Instance;
			if (!settings)
			{
				Debug.LogError($"{LogPrefix} OpenXRSettings.Instance is null. Is OpenXR enabled as plug-in provider for this platform?");
				return;
			}

			var feature = settings.GetFeature<SpatialAnchorFeature>();
			if (!feature) Debug.LogError($"{LogPrefix} SpatialAnchorFeature not found in OpenXR settings for this platform.");
			else if (!feature.enabled) Debug.LogError($"{LogPrefix} 'Spatial Entities: Anchors' is disabled. Enable it under XR Plug-in Management > OpenXR > Spatial Entities.");
			else Debug.Log($"{LogPrefix} 'Spatial Entities: Anchors' feature is enabled.");
		}

		private void LogStatus()
		{
			if (Time.unscaledTime < nextStatusLogTime) return;
			nextStatusLogTime = Time.unscaledTime + StatusLogInterval;

			if (subsystem == null) Debug.LogWarning($"{LogPrefix} Status: no subsystem. Permission granted: {Permission.HasUserAuthorizedPermission(GetPermission())}, requesting: {requestingPermission}");
			else Debug.Log($"{LogPrefix} Status: subsystem running {subsystem.running}, starting {starting}, started by tracker {startedByTracker}, anchors {anchors.Count}, pending {pendingAnchors.Count}, stored {store.Anchors.Count}, pending erase {store.PendingErase.Count}");
		}

		private static void LogCapabilities(SpatialAnchorSubsystem target)
		{
			var descriptor = target.subsystemDescriptor;
			Debug.Log($"{LogPrefix} Capabilities: save {descriptor.supportsSaveAnchor}, load {descriptor.supportsLoadAnchor}, erase {descriptor.supportsEraseAnchor}, saved ids {descriptor.supportsGetSavedAnchorIds}, cancellation {descriptor.supportsAsyncCancellation}");
			if (!descriptor.supportsSaveAnchor) Debug.LogError($"{LogPrefix} The runtime can't save anchors. Anchors will only exist for this session.");
			if (!descriptor.supportsEraseAnchor) Debug.LogError($"{LogPrefix} The runtime can't erase anchors. Deleted and moved anchors will stay on the device.");
		}

		private static void LogAllAnchorSubsystems()
		{
			SubsystemManager.GetSubsystems(AnyAnchorSubsystemBuffer);
			if (AnyAnchorSubsystemBuffer.Count == 0)
			{
				Debug.LogError($"{LogPrefix} No XRAnchorSubsystem of any provider exists. OpenXR may not have initialized.");
				return;
			}

			foreach (var system in AnyAnchorSubsystemBuffer) Debug.Log($"{LogPrefix} Found anchor subsystem '{system.GetType().FullName}', id '{system.subsystemDescriptor.id}', running {system.running}");
			if (AnyAnchorSubsystemBuffer.Count > 1) Debug.LogWarning($"{LogPrefix} Multiple anchor subsystems exist. AR Foundation supports one provider per feature; disable the other provider's anchor feature.");
		}

		private static string Describe(XRResultStatus status) => $"{status} (statusCode {status.statusCode}, nativeStatusCode {status.nativeStatusCode})";

		private static string GetPermission()
		{
			if (SystemInfo.deviceModel.Contains("Oculus") || SystemInfo.deviceModel.Contains("Quest")) return "com.oculus.permission.USE_SCENE";
			return "android.permission.SCENE_UNDERSTANDING_COARSE";
		}

		private void RequestPermission(string permission)
		{
			if (requestingPermission) return;
			requestingPermission = true;
			Debug.Log($"{LogPrefix} Requesting permission '{permission}'...");

			var callbacks = new PermissionCallbacks();
			callbacks.PermissionGranted += p =>
			{
				requestingPermission = false;
				Debug.Log($"{LogPrefix} Permission '{p}' granted.");
				if (isActiveAndEnabled && lifetime != null) StartSubsystem(lifetime.Token);
				else Debug.LogWarning($"{LogPrefix} Permission granted while tracker was disabled. Subsystem will start on next enable.");
			};
			callbacks.PermissionDenied += p =>
			{
				requestingPermission = false;
				Debug.LogError($"{LogPrefix} Permission '{p}' denied. Anchor tracking is disabled.");
				enabled = false;
			};
			callbacks.PermissionDeniedAndDontAskAgain += p =>
			{
				requestingPermission = false;
				Debug.LogError($"{LogPrefix} Permission '{p}' denied permanently. Grant it in the headset's app settings or reinstall the app.");
				enabled = false;
			};
			Permission.RequestUserPermission(permission, callbacks);
		}

		private async void StartSubsystem(CancellationToken token)
		{
			await Awaitable.MainThreadAsync();
			if (starting || token.IsCancellationRequested) return;
			LogAllAnchorSubsystems();

			subsystem = FindSubsystem();
			if (subsystem == null)
			{
				Debug.LogError($"{LogPrefix} No SpatialAnchorSubsystem found. Is 'Spatial Entities: Anchors' enabled for this platform, and is OpenXR initialized?");
				return;
			}

			starting = true;
			var target = subsystem;

			try
			{
				if (target.running) Debug.Log($"{LogPrefix} SpatialAnchorSubsystem was already running (started by something else).");
				else if (!await TryStartSubsystemAsync(target, token)) return;
				if (token.IsCancellationRequested) return;

				LogCapabilities(target);
				await LoadAnchorsAsync(target, token);
				if (token.IsCancellationRequested) return;

				starting = false;
				PersistPendingAnchors();
			}
			catch (OperationCanceledException)
			{
				Debug.LogWarning($"{LogPrefix} Starting or loading was canceled because the tracker was disabled.");
			}
			catch (Exception exception)
			{
				Debug.LogError($"{LogPrefix} Starting or loading threw {exception.GetType().Name}: {exception.Message}");
				Debug.LogException(exception);
			}
			finally
			{
				starting = false;
			}
		}

		private async Awaitable<bool> TryStartSubsystemAsync(SpatialAnchorSubsystem target, CancellationToken token)
		{
			Debug.Log($"{LogPrefix} Subsystem permission state before start: {target.GetPermissionState()}");
			Debug.Log($"{LogPrefix} Starting SpatialAnchorSubsystem with TryStartAsync...");

			var status = await target.TryStartAsync(token);
			if (!status.IsSuccess())
			{
				Debug.LogError($"{LogPrefix} TryStartAsync failed: {Describe(status)}. Permission state: {target.GetPermissionState()}");
				return false;
			}

			if (token.IsCancellationRequested)
			{
				Debug.LogWarning($"{LogPrefix} Tracker was disabled while starting. Stopping the subsystem again.");
				if (target.running) target.Stop();
				return false;
			}

			startedByTracker = true;
			if (target.running) Debug.Log($"{LogPrefix} TryStartAsync succeeded. Subsystem is running.");
			else Debug.LogWarning($"{LogPrefix} TryStartAsync reported success but the subsystem is not running. Watch the status logs.");
			return true;
		}

		private static SpatialAnchorSubsystem FindSubsystem()
		{
			SubsystemManager.GetSubsystems(SubsystemBuffer);
			return SubsystemBuffer.Count > 0 ? SubsystemBuffer[0] : null;
		}

		private async Awaitable LoadAnchorsAsync(SpatialAnchorSubsystem target, CancellationToken token)
		{
			LoadStore();

			var descriptor = target.subsystemDescriptor;
			if (!descriptor.supportsGetSavedAnchorIds || !descriptor.supportsLoadAnchor)
			{
				Debug.LogError($"{LogPrefix} The runtime can't list or load saved anchors. Skipping load.");
				return;
			}

			Debug.Log($"{LogPrefix} Loading saved anchor ids...");
			var idsResult = await target.TryGetSavedAnchorIdsAsync(Allocator.Persistent, token);
			var savedIds = idsResult.value;

			try
			{
				if (!idsResult.status.IsSuccess())
				{
					Debug.LogError($"{LogPrefix} TryGetSavedAnchorIdsAsync failed: {Describe(idsResult.status)}");
					return;
				}

				Debug.Log($"{LogPrefix} Found {savedIds.Length} saved anchor(s) on the device.");
				var runtimeKeys = new HashSet<string>();
				for (var i = 0; i < savedIds.Length; i++)
				{
					if (token.IsCancellationRequested) return;

					var savedGuid = savedIds[i];
					var key = savedGuid.guid.ToString();
					runtimeKeys.Add(key);

					if (store.PendingErase.Contains(key))
					{
						Debug.Log($"{LogPrefix} Saved anchor {key} is marked for erase (left over from a move or delete). Erasing.");
						EraseSaved(savedGuid);
						continue;
					}

					if (!TryGetStoredAnchorId(key, out var id))
					{
						id = savedGuid.guid;
						SetStoredAnchor(key, id);
						Debug.LogWarning($"{LogPrefix} Saved anchor {key} is not in the store file. Loading it with that id.");
					}

					await LoadAnchorAsync(target, savedGuid, id, token);
				}

				PruneStore(runtimeKeys);
				WriteStore();
			}
			finally
			{
				if (savedIds.IsCreated) savedIds.Dispose();
			}
		}

		private async Awaitable LoadAnchorAsync(SpatialAnchorSubsystem target, SerializableGuid savedGuid, Guid id, CancellationToken token)
		{
			var result = await target.TryLoadAnchorAsync(savedGuid, token);
			if (token.IsCancellationRequested)
			{
				if (result.status.IsSuccess() && target.running) target.TryRemoveAnchor(result.value.trackableId);
				return;
			}

			if (!result.status.IsSuccess())
			{
				Debug.LogError($"{LogPrefix} Failed to load saved anchor {id} (saved as {savedGuid.guid}): {Describe(result.status)}");
				return;
			}

			var runtimeAnchor = result.value;
			var anchor = CreateAnchorObject("Spatial Anchor (Loaded)");
			anchor.Initialize(this, id, ToWorld(runtimeAnchor.pose.position));
			anchors.Add(anchor);
			records.Add(anchor, new AnchorRecord { TrackableId = runtimeAnchor.trackableId, SavedGuid = savedGuid, IsSaved = true });
			anchorsByTrackable[runtimeAnchor.trackableId] = anchor;
			Debug.Log($"{LogPrefix} Loaded anchor {anchor.ID} (trackable {runtimeAnchor.trackableId}, tracking {runtimeAnchor.trackingState}) at {anchor.Position}.");
			OnAnchorLoaded?.Invoke(anchor);
		}

		private void SchedulePersist(XRAnchor anchor)
		{
			var record = records[anchor];
			if (record.IsPersisting) return;

			if (subsystem is not { running: true } || starting || lifetime == null)
			{
				if (!pendingAnchors.Contains(anchor)) pendingAnchors.Add(anchor);
				Debug.Log($"{LogPrefix} Anchor {anchor.ID} queued. It will be stored once the subsystem is running and saved anchors are loaded.");
				return;
			}

			PersistAnchor(anchor, record, lifetime.Token);
		}

		private void PersistPendingAnchors()
		{
			if (pendingAnchors.Count == 0) return;
			Debug.Log($"{LogPrefix} Storing {pendingAnchors.Count} queued anchor(s).");

			var queued = pendingAnchors.ToArray();
			pendingAnchors.Clear();
			foreach (var anchor in queued) SchedulePersist(anchor);
		}

		private async void PersistAnchor(XRAnchor anchor, AnchorRecord record, CancellationToken token)
		{
			var target = subsystem;
			if (target is not { running: true })
			{
				Debug.LogError($"{LogPrefix} Can't store anchor {anchor.ID}: subsystem is not running.");
				return;
			}

			var id = anchor.ID;
			record.IsPersisting = true;

			try
			{
				while (true)
				{
					var version = record.Version;
					var pose = new Pose(ToSession(anchor.Position), Quaternion.identity);
					var addResult = await target.TryAddAnchorAsync(pose);
					if (!addResult.status.IsSuccess())
					{
						Debug.LogError($"{LogPrefix} TryAddAnchorAsync failed for anchor {id}: {Describe(addResult.status)}");
						return;
					}

					var trackableId = addResult.value.trackableId;
					if (record.IsDeleted || token.IsCancellationRequested || !anchor)
					{
						if (target.running) target.TryRemoveAnchor(trackableId);
						Debug.Log($"{LogPrefix} Anchor {id} was deleted or the tracker was disabled while adding. Removed runtime anchor {trackableId}.");
						return;
					}

					Debug.Log($"{LogPrefix} Runtime anchor {trackableId} added for anchor {id}.");

					if (!target.subsystemDescriptor.supportsSaveAnchor)
					{
						ReplaceRuntimeAnchor(anchor, record, trackableId, version, target);
						Debug.LogWarning($"{LogPrefix} Anchor {id} not saved: runtime doesn't support saving.");
						if (record.Version == version) return;
						continue;
					}

					var saveResult = await target.TrySaveAnchorAsync(trackableId, token);
					if (!saveResult.status.IsSuccess())
					{
						Debug.LogError($"{LogPrefix} TrySaveAnchorAsync failed for anchor {id}: {Describe(saveResult.status)}. The new position exists for this session only.");
						if (anchor) ReplaceRuntimeAnchor(anchor, record, trackableId, version, target);
						return;
					}

					var savedGuid = saveResult.value;
					if (record.IsDeleted)
					{
						if (target.running) target.TryRemoveAnchor(trackableId);
						Debug.Log($"{LogPrefix} Anchor {id} was deleted while saving. Erasing the new save.");
						EraseSaved(savedGuid);
						return;
					}

					if (token.IsCancellationRequested || !anchor)
					{
						CommitSavedAnchor(id, record, savedGuid);
						return;
					}

					ReplaceRuntimeAnchor(anchor, record, trackableId, version, target);
					CommitSavedAnchor(id, record, savedGuid);
					OnAnchorSaved?.Invoke(anchor);
					if (record.Version == version) return;

					Debug.Log($"{LogPrefix} Anchor {id} moved while saving. Recreating at the latest position.");
				}
			}
			catch (OperationCanceledException)
			{
				Debug.LogWarning($"{LogPrefix} Storing anchor {id} was canceled because the tracker was disabled.");
			}
			catch (Exception exception)
			{
				Debug.LogError($"{LogPrefix} Storing anchor {id} threw {exception.GetType().Name}: {exception.Message}");
				Debug.LogException(exception);
			}
			finally
			{
				record.IsPersisting = false;
			}
		}

		private void ReplaceRuntimeAnchor(XRAnchor anchor, AnchorRecord record, TrackableId trackableId, int version, SpatialAnchorSubsystem target)
		{
			var oldTrackableId = record.TrackableId;
			record.TrackableId = trackableId;
			if (record.Version == version) anchorsByTrackable[trackableId] = anchor;
			if (oldTrackableId == TrackableId.invalidId) return;

			anchorsByTrackable.Remove(oldTrackableId);
			if (target.running && !target.TryRemoveAnchor(oldTrackableId)) Debug.LogWarning($"{LogPrefix} TryRemoveAnchor failed for old runtime anchor {oldTrackableId}.");
		}

		private void CommitSavedAnchor(Guid id, AnchorRecord record, SerializableGuid savedGuid)
		{
			var hadOldSave = record.IsSaved;
			var oldSavedGuid = record.SavedGuid;
			record.SavedGuid = savedGuid;
			record.IsSaved = true;
			SetStoredAnchor(savedGuid.guid.ToString(), id);
			Debug.Log($"{LogPrefix} Saved anchor {id} as {savedGuid.guid}.");

			if (hadOldSave) EraseSaved(oldSavedGuid);
			else WriteStore();
		}

		private async void EraseSaved(SerializableGuid savedGuid)
		{
			var key = savedGuid.guid.ToString();
			RemoveStoredAnchor(key);
			if (!store.PendingErase.Contains(key)) store.PendingErase.Add(key);
			WriteStore();

			var target = subsystem;
			if (target is not { running: true })
			{
				Debug.LogError($"{LogPrefix} Can't erase saved anchor {key} now: subsystem is not running. It will be erased on the next load.");
				return;
			}

			try
			{
				var status = await target.TryEraseAnchorAsync(savedGuid, CancellationToken.None);
				if (!status.IsSuccess())
				{
					Debug.LogError($"{LogPrefix} TryEraseAnchorAsync failed for {key}: {Describe(status)}. It will be retried on the next load.");
					return;
				}

				store.PendingErase.Remove(key);
				WriteStore();
				Debug.Log($"{LogPrefix} Erased saved anchor {key}.");
			}
			catch (Exception exception)
			{
				Debug.LogError($"{LogPrefix} Erasing saved anchor {key} threw {exception.GetType().Name}: {exception.Message}");
				Debug.LogException(exception);
			}
		}

		private void LoadStore()
		{
			store = new AnchorStore();
			if (!File.Exists(StorePath))
			{
				Debug.Log($"{LogPrefix} No store file at '{StorePath}'. Starting empty.");
				return;
			}

			try
			{
				store = JsonUtility.FromJson<AnchorStore>(File.ReadAllText(StorePath)) ?? new AnchorStore();
				store.Anchors ??= new List<AnchorEntry>();
				store.PendingErase ??= new List<string>();
				Debug.Log($"{LogPrefix} Loaded store file '{StorePath}': {store.Anchors.Count} anchor(s), {store.PendingErase.Count} pending erase.");
			}
			catch (Exception exception)
			{
				store = new AnchorStore();
				Debug.LogError($"{LogPrefix} Store file '{StorePath}' could not be read. Saved anchors will load with their device ids. {exception.GetType().Name}: {exception.Message}");
			}
		}

		private void WriteStore()
		{
			try
			{
				File.WriteAllText(StorePath, JsonUtility.ToJson(store, true));
			}
			catch (Exception exception)
			{
				Debug.LogError($"{LogPrefix} Writing store file '{StorePath}' failed. {exception.GetType().Name}: {exception.Message}");
			}
		}

		private bool TryGetStoredAnchorId(string savedKey, out Guid id)
		{
			foreach (var entry in store.Anchors) if (entry.SavedGuid == savedKey) return Guid.TryParse(entry.Id, out id);
			id = Guid.Empty;
			return false;
		}

		private void SetStoredAnchor(string savedKey, Guid id)
		{
			foreach (var entry in store.Anchors)
			{
				if (entry.SavedGuid != savedKey) continue;
				entry.Id = id.ToString();
				return;
			}

			store.Anchors.Add(new AnchorEntry { SavedGuid = savedKey, Id = id.ToString() });
		}

		private void RemoveStoredAnchor(string savedKey)
		{
			for (var i = store.Anchors.Count - 1; i >= 0; i--) if (store.Anchors[i].SavedGuid == savedKey) store.Anchors.RemoveAt(i);
		}

		private void PruneStore(HashSet<string> runtimeKeys)
		{
			for (var i = store.Anchors.Count - 1; i >= 0; i--)
			{
				if (runtimeKeys.Contains(store.Anchors[i].SavedGuid)) continue;
				Debug.LogWarning($"{LogPrefix} Stored anchor {store.Anchors[i].Id} no longer exists on the device. Removing it from the store.");
				store.Anchors.RemoveAt(i);
			}

			for (var i = store.PendingErase.Count - 1; i >= 0; i--) if (!runtimeKeys.Contains(store.PendingErase[i])) store.PendingErase.RemoveAt(i);
		}

		private void UpdateAnchorPose(XRSubsystemAnchor runtimeAnchor)
		{
			if (runtimeAnchor.trackingState is UnityEngine.XR.ARSubsystems.TrackingState.None) return;
			if (!anchorsByTrackable.TryGetValue(runtimeAnchor.trackableId, out var anchor)) return;
			if (!anchor) return;

			anchor.transform.position = ToWorld(runtimeAnchor.pose.position);
		}

		private void ReleaseAnchors()
		{
			var removeFromRuntime = subsystem is { running: true } && !startedByTracker;
			foreach (var anchor in anchors)
			{
				if (!records.TryGetValue(anchor, out var record)) continue;
				if (!record.IsSaved) Debug.LogWarning($"{LogPrefix} Anchor {anchor.ID} was not saved yet and is lost.");
				if (removeFromRuntime && record.TrackableId != TrackableId.invalidId) subsystem.TryRemoveAnchor(record.TrackableId);
				if (anchor) Destroy(anchor.gameObject);
			}

			anchors.Clear();
			pendingAnchors.Clear();
			records.Clear();
			anchorsByTrackable.Clear();
		}

		private XRAnchor CreateAnchorObject(string objectName)
		{
			if (!Rig) Debug.LogError($"{LogPrefix} Rig is null. Anchor will be created at the scene root.");

			var go = new GameObject(objectName);
			if (Rig) go.transform.SetParent(Rig.transform, false);
			return go.AddComponent<XRAnchor>();
		}

		private Vector3 ToWorld(Vector3 sessionPosition) => Rig ? Rig.transform.TransformPoint(sessionPosition) : sessionPosition;

		private Vector3 ToSession(Vector3 worldPosition) => Rig ? Rig.transform.InverseTransformPoint(worldPosition) : worldPosition;

		private class AnchorRecord
		{
			public TrackableId TrackableId { get; set; } = TrackableId.invalidId;
			public SerializableGuid SavedGuid { get; set; }
			public bool IsSaved { get; set; }
			public bool IsDeleted { get; set; }
			public bool IsPersisting { get; set; }
			public int Version { get; set; }
		}

		[Serializable]
		private class AnchorStore
		{
			public List<AnchorEntry> Anchors = new();
			public List<string> PendingErase = new();
		}

		[Serializable]
		private class AnchorEntry
		{
			public string Id;
			public string SavedGuid;
		}
	}
}