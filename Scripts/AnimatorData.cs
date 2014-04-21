using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

using Holoville.HOTween;

[ExecuteInEditMode]
[AddComponentMenu("M8/Animator")]
public class AnimatorData : MonoBehaviour, AMITarget {
    public enum DisableAction {
        None,
        Pause,
        Stop
    }

    public delegate void OnTake(AnimatorData anim, AMTakeData take);

    // show

	// obsolete stuff
	[SerializeField]
    List<AMTake> takes = new List<AMTake>();
	[SerializeField]
    AMTake playOnStart = null;
	//

	[SerializeField]
	List<AMTakeData> takeData = new List<AMTakeData>();
	[SerializeField]
	int playOnStartIndex = -1;

	[SerializeField]
	AnimatorMeta meta; //
	[SerializeField]
	string playOnStartMeta; //used for playing a take from AnimatorMeta

    public bool sequenceLoadAll = true;
    public bool sequenceKillWhenDone = false;

    public bool playOnEnable = false;

	public bool isGlobal = false;

    public DisableAction onDisableAction = DisableAction.Pause;

    public UpdateType updateType = UpdateType.Update;
    // hide

    public event OnTake takeCompleteCallback;

	public string defaultTakeName {
		get {
			if(meta)
				return playOnStartMeta;
			else
				return playOnStartIndex == -1 ? "" : takeData[playOnStartIndex].name;
		}
		set {
			if(meta) {
				playOnStartMeta = value;
				playOnStartIndex = -1;
			}
			else {
				playOnStartMeta = "";
				playOnStartIndex = -1;
				if(!string.IsNullOrEmpty(value)) {
					List<AMTakeData> _ts = _takes;
					for(int i = 0; i < _ts.Count; i++) {
						if(_ts[i].name == value) {
							playOnStartIndex = i;
							break;
						}
					}
				}
				//
			}
		}
	}

    public bool isPlaying {
        get {
            Sequence seq = currentPlayingSequence;
            return seq != null && !(seq.isPaused || seq.isComplete);
        }
    }

    public bool isPaused {
        get {
			Sequence seq = currentPlayingSequence;
			return seq != null && seq.isPaused;
        }
    }

    public bool isReversed {
        set {
			Sequence seq = currentPlayingSequence;
			if(seq != null) {
                if(value) {
					if(!seq.isReversed)
						seq.Reverse();
                }
                else {
					if(seq.isReversed)
						seq.Reverse();
                }
            }
        }

        get {
			Sequence seq = currentPlayingSequence;
			return seq != null && seq.isReversed;
        }
    }

    public string takeName {
        get {
			AMTakeData take = mCurrentPlayingTake;
			if(take != null) return take.name;
            return "";
        }
    }

    public float runningTime {
        get {
			Sequence seq = currentPlayingSequence;
			return seq != null ? seq.elapsed : 0.0f;
        }
    }
    public float totalTime {
        get {
			AMTakeData take = mCurrentPlayingTake;
			if(take == null) return 0f;
			else return (float)take.numFrames / (float)take.frameRate;
        }
    }
        
    [System.NonSerialized]
    public bool isInspectorOpen = false;
    [HideInInspector]
    public float zoom = 0.4f;
    [HideInInspector]
    public int codeLanguage = 0; 	// 0 = C#, 1 = Javascript
    [HideInInspector]
    public float width_track = 150f;
        
    [HideInInspector]
    [SerializeField]
    private GameObject _dataHolder;

	private Sequence[] mSequences;

    private int mNowPlayingTakeIndex = -1;

    //private bool isLooping = false;
    //private float takeTime = 0f;
    private bool mStarted = false;
        
    private float mAnimScale = 1.0f; //NOTE: this is reset during disable

	private Dictionary<string, Transform> mCache;

	private AMTakeData mCurrentPlayingTake { get { return mNowPlayingTakeIndex == -1 ? null : _takes[mNowPlayingTakeIndex]; } }

	public string currentPlayingTakeName { get { return mNowPlayingTakeIndex == -1 ? "" : mCurrentPlayingTake.name; } }
	public Sequence currentPlayingSequence { get { return mNowPlayingTakeIndex == -1 ? null : mSequences[mNowPlayingTakeIndex]; } }

    public float animScale {
        get { return mAnimScale; }
        set {
            if(mAnimScale != value) {
                mAnimScale = value;
				Sequence seq = currentPlayingSequence;
				if(seq != null)
                    seq.timeScale = mAnimScale;
            }
        }
    }

	public List<AMTakeData> _takes {
        get { return meta ? meta.takes : takeData; }
	}

    public void PlayDefault(bool loop = false) {
        if(!string.IsNullOrEmpty(defaultTakeName)) {
			Play(defaultTakeName, loop);
		}
    }

    // play take by name
    public void Play(string takeName, bool loop = false) {
		PlayAtFrame(takeName, 0f, loop);
    }

	public void PlayAtFrame(string takeName, float frame, bool loop = false) {
		int ind = getTakeIndex(takeName);
		if(ind == -1) { Debug.LogError("Take not found: "+takeName); return; }
		Play(ind, true, frame, loop);
	}
	
	public void PlayAtTime(string takeName, float time, bool loop = false) {
		int ind = getTakeIndex(takeName);
		if(ind == -1) { Debug.LogError("Take not found: "+takeName); return; }
		Play(ind, false, time, loop);
    }


    public void Pause() {
		AMTakeData take = mCurrentPlayingTake;
		if(take == null) return;
		take.stopAudio(this);

		Sequence seq = currentPlayingSequence;
		if(seq != null)
			seq.Pause();
    }

    public void Resume() {
		Sequence seq = currentPlayingSequence;
		if(seq != null)
			seq.Play();
    }

    public void Stop() {
		AMTakeData take = mCurrentPlayingTake;
		if(take == null) return;
		take.stopAudio(this);
		take.stopAnimations(this);

		Sequence seq = currentPlayingSequence;
		if(seq != null) {
			seq.Pause();
			seq.GoTo(0);
        }

        mNowPlayingTakeIndex = -1;
    }

    public void GotoFrame(float frame) {
		AMTakeData take = mCurrentPlayingTake;
		Sequence seq = currentPlayingSequence;
		if(take != null && seq != null) {
            float t = frame / take.frameRate;
            seq.GoTo(t);
        }
        else {
            Debug.LogWarning("No take playing...");
        }
    }

    public void Reverse() {
		Sequence seq = currentPlayingSequence;
        if(seq != null)
			seq.Reverse();
    }

    // preview a single frame (used for scrubbing)
    public void PreviewFrame(string takeName, float frame) {
        PreviewValue(takeName, true, frame);
    }

    // preview a single time (used for scrubbing)
    public void PreviewTime(string takeName, float time) {
        PreviewValue(takeName, false, time);
    }

    void Play(int index, bool isFrame, float value, bool loop) {
		AMTakeData newPlayTake = _takes[index];

        if(newPlayTake == null) {
            Stop();
            return;
        }

        if(mNowPlayingTakeIndex != index) {
            Pause();
        }

		mNowPlayingTakeIndex = index;

        float startTime = value;
		if(isFrame) startTime /= newPlayTake.frameRate;

        //float startFrame = 0;//isFrame ? value : nowPlayingTake.frameRate * value;

		Sequence seq = mSequences[index];

		if(seq == null) {
			//newPlayTake.previewFrame(startFrame, false, true);
			seq = mSequences[index] = newPlayTake.BuildSequence(this, gameObject.name, sequenceKillWhenDone, updateType, OnTakeSequenceDone);
		}

		newPlayTake.previewFrameStart(this, isFrame ? value : newPlayTake.frameRate * value);

		if(seq != null) {
            if(loop) {
				seq.loops = -1;
            }
            else {
				seq.loops = newPlayTake.numLoop;
            }

			seq.GoTo(startTime);
			seq.Play();
			seq.timeScale = mAnimScale;
        }
    }

    void PreviewValue(string take_name, bool isFrame, float value) {
		AMTakeData curTake = mCurrentPlayingTake;
		AMTakeData take = curTake != null && curTake.name == takeName ? curTake : takeData[getTakeIndex(take_name)];
		if(take == null) return;
		float startFrame = value;
		if(!isFrame) startFrame *= take.frameRate;	// convert time to frame
		take.previewFrame(this, startFrame);
	}
	    
	void OnDestroy() {
#if UNITY_EDITOR
		if(!Application.isPlaying) {
			if(_dataHolder) {
				UnityEditor.Undo.DestroyObjectImmediate(_dataHolder);
			}
		}
#endif

		if(mSequences != null) {
			for(int i = 0; i < mSequences.Length; i++) {
				HOTween.Kill(mSequences[i]);
				mSequences[i] = null;
			}
		}
		
		takeCompleteCallback = null;
	}
	
	void OnEnable() {
		if(mStarted) {
			if(playOnEnable) {
				if(mNowPlayingTakeIndex == -1 && !string.IsNullOrEmpty(defaultTakeName))
					Play(defaultTakeName, false);
				else
					Resume();
			}
			//else if(playOnStart) {
			//Play(playOnStart.name, true, 0f, false);
			//}
		}
	}
	
	void OnDisable() {
		switch(onDisableAction) {
		case DisableAction.Pause:
			Pause();
			break;
		case DisableAction.Stop:
			Stop();
			break;
		}
		
		mAnimScale = 1.0f;
	}
	
	void Awake() {
		Upgrade();
		
		if(!Application.isPlaying)
			return;
		
		mSequences = new Sequence[_takes.Count];
	}
	
	void Start() {
		if(!Application.isPlaying)
			return;

		List<AMTakeData> _ts = _takes;

		mStarted = true;
		if(sequenceLoadAll && _ts != null) {
			string goName = gameObject.name;
			for(int i = 0; i < _ts.Count; i++) {
				mSequences[i] = _ts[i].BuildSequence(this, goName, sequenceKillWhenDone, updateType, OnTakeSequenceDone);
			}
		}
		
		if(!string.IsNullOrEmpty(defaultTakeName)) {
			Play(defaultTakeName, false);
		}
	}
			
	//returns true if upgraded
	public bool Upgrade() {
		//convert AMTakes
		if(takes != null && takes.Count > 0) {
			if(playOnStart != null) {
				playOnStartIndex = takes.IndexOf(playOnStart);
				playOnStart = null;
			}
			
			takeData = new List<AMTakeData>(takes.Count);
			foreach(AMTake take in takes) {
				AMTakeData ntake = new AMTakeData();
				ntake.name = take.name;
				ntake.frameRate = take.frameRate;
				ntake.numFrames = take.numFrames;
				ntake.startFrame = take.startFrame;
				ntake.endFrame = take.endFrame;
				ntake.playbackSpeedIndex = take.playbackSpeedIndex;
				ntake.numLoop = take.numLoop;
				ntake.loopMode = take.loopMode;
				ntake.loopBackToFrame = take.loopBackToFrame;
				ntake.trackValues = new List<AMTrack>(take.trackValues.Count);
				foreach(AMTrack track in take.trackValues) ntake.trackValues.Add(track);
				ntake.track_count = take.track_count;
				ntake.group_count = take.group_count;
				ntake.rootGroup = take.rootGroup != null ? take.rootGroup.duplicate() : null;
				ntake.groupValues = new List<AMGroup>(take.groupValues.Count);
				foreach(AMGroup grp in take.groupValues) ntake.groupValues.Add(grp.duplicate());
				
				DestroyImmediate(take);
				
				takeData.Add(ntake);
			}
			
			takes = null;
			
			return true;
		}
		
		return false;
	}

	int getTakeIndex(string takeName) {
		List<AMTakeData> _ts = _takes;
		for(int i = 0; i < _ts.Count; i++) {
			if(_ts[i].name == takeName)
				return i;
		}
		return -1;
	}

	void OnTakeSequenceDone(AMTakeData aTake) {
		if(takeCompleteCallback != null)
			takeCompleteCallback(this, aTake);
	}

    #region AMITarget interface
    public Transform TargetGetRoot() {
		return transform;
	}

	public Transform TargetGetDataHolder() {
		if(meta) {
			return meta.transform;
		}
		else {
			if(_dataHolder == null) {
				foreach(Transform child in transform) {
					if(child.gameObject.name == "_animdata") {
						_dataHolder = child.gameObject;
						break;
					}
				}
				
				if(_dataHolder) {
					//refresh data?
				}
				else {
					_dataHolder = new GameObject("_animdata");
					_dataHolder.transform.parent = transform;
					_dataHolder.SetActive(false);
				}
			}
			
			return _dataHolder.transform;
		}
	}

	public bool TargetIsMeta() {
		return meta != null;
	}

#if UNITY_EDITOR
	private HashSet<string> mTargetMissing;
	public void TargetMissing(string path, bool isMissing) {
		if(mTargetMissing == null) mTargetMissing = new HashSet<string>();
		if(isMissing) {
			mTargetMissing.Add(path);

			if(Application.isPlaying)
				Debug.LogWarning(name+ " is missing Target: "+path);
		}
		else
			mTargetMissing.Remove(path);
	}
#else
	public void TargetMissing(string path, bool isMissing) {
		if(isMissing)
			Debug.LogWarning(name+ " is missing Target: "+path);
	}
#endif

	public Transform TargetGetCache(string path) {
		Transform ret = null;
		if(mCache != null) {
			if(mCache.TryGetValue(path, out ret)) {
#if UNITY_EDITOR
				if(mTargetMissing != null)
					mTargetMissing.Remove(path);
#endif
			}
		}
		return ret;
	}
	
	public void TargetSetCache(string path, Transform obj) {
		if(mCache == null) mCache = new Dictionary<string, Transform>();
		if(mCache.ContainsKey(path))
			mCache[path] = obj;
		else
			mCache.Add(path, obj);
#if UNITY_EDITOR
		if(mTargetMissing != null)
			mTargetMissing.Remove(path);
#endif
	}

    #endregion

    #region Editor stuff
#if UNITY_EDITOR
    [System.NonSerialized]
    public bool e_autoKey = false;
    [System.NonSerialized]
    public bool e_isAnimatorOpen = false;
    [System.NonSerialized]
    public int e_currentTake;
    [System.NonSerialized]
    public int e_prevTake = -1;

	void OnDrawGizmos() {
        if(!e_isAnimatorOpen || _takes == null || _takes.Count == 0) return;
        if(e_currentTake < 0) {
            e_currentTake = 0;
        }
        else if(e_currentTake >= _takes.Count)
            e_currentTake = _takes.Count - 1;

		_takes[e_currentTake].drawGizmos(this, AnimatorTimeline.e_gizmoSize, Application.isPlaying);
	}

	public string[] e_getMissingTargets() {
		if(mTargetMissing != null)
			return mTargetMissing.ToArray();
		else
			return new string[0];
	}

    public void e_maintainTargetCache(AMTrack track) {
        if(TargetIsMeta() && mCache.ContainsKey(track.targetPath)) {
            UnityEngine.Object obj = track.GetTarget(this);
            if(obj) {
                string objPath = AMUtil.GetPath(transform, obj);
                if(objPath != track.targetPath) {
                    mCache.Remove(track.targetPath);
                }
            }
        }
    }

	public void e_maintainTakes() {
		foreach(AMTakeData take in _takes) {
			take.maintainTake(this);
		}

		if(mCache != null)
			mCache.Clear();
	}

	public bool e_isCurrentTakePlayOnStart {
		get {
			if(meta) {
				AMTakeData cur = e_getCurrentTake();
				if(cur != null) {
					return playOnStartMeta == cur.name;
				}
			}
			else
				return playOnStartIndex == e_currentTake;

			return false;
		}
	}

	public AnimatorMeta e_meta { 
		get { return meta; }
	}

    public bool e_metaCanInstantiatePrefab {
        get {
            if(meta) {
                if(UnityEditor.PrefabUtility.GetPrefabType(meta) == UnityEditor.PrefabType.Prefab) {
                    GameObject go = UnityEditor.PrefabUtility.FindRootGameObjectWithSameParentPrefab(meta.gameObject);
                    return go && go.GetComponent<AnimatorMeta>() != null;
                }
            }
            return false;
        }
    }

    public bool e_metaCanSavePrefabInstance {
        get {
            if(meta && UnityEditor.PrefabUtility.GetPrefabType(meta) == UnityEditor.PrefabType.PrefabInstance) {
                GameObject go = UnityEditor.PrefabUtility.FindRootGameObjectWithSameParentPrefab(meta.gameObject);
                return go && go.GetComponent<AnimatorMeta>() != null;
            }
            return false;
        }
    }

    /// <summary>
    /// For editing the animator meta
    /// </summary>
    public bool e_metaInstantiatePrefab(string undoLabel) {
        if(e_metaCanInstantiatePrefab) {
            GameObject go = UnityEditor.PrefabUtility.InstantiatePrefab(meta.gameObject) as GameObject;
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, undoLabel);
            UnityEditor.Undo.SetTransformParent(go.transform, transform, undoLabel);
            UnityEditor.Undo.RegisterCompleteObjectUndo(this, undoLabel);
            meta = go.GetComponent<AnimatorMeta>();
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }
        return false;
    }

    public void e_metaSaveInstantiate() {
        if(meta && UnityEditor.PrefabUtility.GetPrefabType(meta) == UnityEditor.PrefabType.PrefabInstance) {
            GameObject instanceGO = meta.gameObject;
            GameObject prefab = UnityEditor.PrefabUtility.GetPrefabParent(instanceGO) as GameObject;
            if(prefab) {
                UnityEditor.Undo.RegisterCompleteObjectUndo(this, "Save Prefab");

                UnityEditor.PrefabUtility.ReplacePrefab(instanceGO, prefab);
                meta = prefab.GetComponent<AnimatorMeta>();

                UnityEditor.Undo.DestroyObjectImmediate(instanceGO);
            }
        }
    }

	/// <summary>
	/// if copyTakes is true, overrides all takes in newMeta (if null, then to our dataholder) with current data
	/// </summary>
	public void e_setMeta(AnimatorMeta newMeta, bool copyTakes) {
		if(meta != newMeta) {
            AnimatorMeta prevMeta = meta;
			List<AMTakeData> prevTakes = _takes;
			string prevPlayOnStartName = defaultTakeName;

			meta = newMeta;

			if(meta) {
				if(copyTakes) {
                    meta.takes.Clear();

					foreach(AMTakeData take in prevTakes)
                        e_duplicateTake(take, true);
				}

				//clear out non-meta stuff
				if(_dataHolder) {
					UnityEditor.Undo.DestroyObjectImmediate(_dataHolder);
					_dataHolder = null;
				}
				takeData.Clear();
			}
			else {
				//create data holder
				_dataHolder = new GameObject("_animdata");
				_dataHolder.transform.parent = transform;
				_dataHolder.SetActive(false);
                UnityEditor.Undo.RegisterCreatedObjectUndo(_dataHolder, "Set Meta");

				if(copyTakes) { //duplicate meta to takeData
					foreach(AMTakeData take in prevTakes)
                        e_duplicateTake(take, true);
				}
			}

			if(_takes == null || _takes.Count == 0) { //add at least one take
				e_addTake();
			}

			//get new play on start
			playOnStartMeta = "";
			playOnStartIndex = -1;

			if(!string.IsNullOrEmpty(prevPlayOnStartName)) {
				string newPlayOnStart = "";
				foreach(AMTakeData take in _takes) {
					if(take.name == prevPlayOnStartName) {
						newPlayOnStart = take.name;
						break;
					}
				}

				defaultTakeName = newPlayOnStart;
			}
			//

			//reset editor data
			if(mTargetMissing != null)
				mTargetMissing.Clear();

            //destroy previous meta if it is not prefab
            if(prevMeta && UnityEditor.PrefabUtility.GetPrefabType(prevMeta) != UnityEditor.PrefabType.Prefab) {
                UnityEditor.Undo.DestroyObjectImmediate(prevMeta.gameObject);
            }
		}
	}

	/// <summary>
	/// attempt to generate the missing targets
	/// </summary>
	public void e_generateMissingTargets() {
		if(mTargetMissing != null && mTargetMissing.Count > 0) {
			foreach(string missingPath in mTargetMissing) {
				AMUtil.CreateTarget(transform, missingPath);
			}

			//fill necessary components per track and key
			foreach(AMTakeData take in _takes) {
				foreach(AMTrack track in take.trackValues) {
					Transform t = AMUtil.GetTarget(transform, track.targetPath);

					string compName = track.GetRequiredComponent();
					if(!string.IsNullOrEmpty(compName)) {
						Component comp = t.gameObject.GetComponent(compName);
						if(comp == null) {
							t.gameObject.AddComponent(compName);
						}
					}

					foreach(AMKey key in track.keys) {
						compName = key.GetRequiredComponent();
						if(!string.IsNullOrEmpty(compName)) {
							Component comp = t.gameObject.GetComponent(compName);
							if(comp == null) {
								t.gameObject.AddComponent(compName);
							}
						}
					}
				}
			}

			mTargetMissing.Clear();

			if(mCache != null)
				mCache.Clear();
		}
	}

	public int e_takeCount { get { return _takes.Count; } }
	
	public int e_getPlayOnStartIndex() {
		return playOnStartIndex;
	}

	public AMTakeData e_getTake(string takeName) {
		int ind = getTakeIndex(takeName);
		if(ind == -1) {
			Debug.LogError("Animator: Take '" + takeName + "' not found.");
			return null;
		}
		
		return _takes[ind];
	}

	public int e_getTakeIndex(AMTakeData take) {
		List<AMTakeData> _ts = _takes;
		for(int i = 0; i < _ts.Count; i++) {
			if(_ts[i] == take) return i;
		}
		return -1;
	}
		
	public bool e_setCurrentTakeValue(int _take) {
		if(_take != e_currentTake) {
			e_prevTake = e_currentTake;
			
			// reset preview to frame 1
			e_getCurrentTake().previewFrame(this, 1f);
			// change take
			e_currentTake = _take;
			return true;
		}
		return false;
	}
	
	public AMTakeData e_getCurrentTake() {
		List<AMTakeData> _ts = _takes;
		if(_ts == null || e_currentTake >= _ts.Count || e_currentTake < 0) return null;
		return _ts[e_currentTake];
    }

    public AMTakeData e_getPreviousTake() {
		List<AMTakeData> _ts = _takes;
        return _ts != null && e_prevTake >= 0 && e_prevTake < _ts.Count ? _ts[e_prevTake] : null;
    }

    public AMTakeData e_addTake() {
		List<AMTakeData> _ts = _takes;
		string name = "Take" + (_ts.Count + 1);
        AMTakeData a = new AMTakeData();
        // set defaults
        a.name = name;
        e_makeTakeNameUnique(a);
        
		_ts.Add(a);
		e_selectTake(_ts.Count - 1);

        return a;
    }

    /// <summary>
    /// This will only duplicate the tracks and groups, includeKeys=true to also duplicate keys
    /// </summary>
    /// <param name="take"></param>
    public void e_duplicateTake(AMTakeData dupTake, bool includeKeys) {
		AMTakeData a = new AMTakeData();

        a.name = dupTake.name;
        e_makeTakeNameUnique(a);
        a.numLoop = dupTake.numLoop;
        a.loopMode = dupTake.loopMode;
        a.frameRate = dupTake.frameRate;
        a.numFrames = dupTake.numFrames;
        a.startFrame = dupTake.startFrame;
        a.playbackSpeedIndex = 2;
        //a.lsTracks = new List<AMTrack>();
        //a.dictTracks = new Dictionary<int,AMTrack>();

        if(dupTake.rootGroup != null) {
            a.rootGroup = dupTake.rootGroup.duplicate();
        }
        else {
            a.initGroups();
        }

        a.group_count = dupTake.group_count;

        if(dupTake.groupValues != null) {
            a.groupValues = new List<AMGroup>();
            foreach(AMGroup grp in dupTake.groupValues) {
                a.groupValues.Add(grp.duplicate());
            }
        }

        a.track_count = dupTake.track_count;

        if(dupTake.trackValues != null) {
            a.trackValues = new List<AMTrack>();
            foreach(AMTrack track in dupTake.trackValues) {
                AMTrack dupTrack = UnityEditor.Undo.AddComponent(TargetGetDataHolder().gameObject, track.GetType()) as AMTrack;
                dupTrack.enabled = false;
                track.CopyTo(dupTrack);
                a.trackValues.Add(dupTrack);

				dupTrack.maintainTrack(this);

				Object tgtObj = dupTrack.GetTarget(this);

				//if there's no target, then we can't add the keys for events and properties
				if(includeKeys && !(tgtObj == null && (dupTrack is AMPropertyTrack || dupTrack is AMEventTrack))) {
					foreach(AMKey key in track.keys) {
                        AMKey dupKey = UnityEditor.Undo.AddComponent(TargetGetDataHolder().gameObject, key.GetType()) as AMKey;
						if(dupKey) {
                            key.CopyTo(dupKey);
                            dupKey.enabled = false;
                            dupKey.maintainKey(this, tgtObj);
							dupTrack.keys.Add(dupKey);
						}
					}

					dupTrack.updateCache(this);
				}
            }
        }

		List<AMTakeData> _ts = _takes;
		_ts.Add(a);
		e_selectTake(_ts.Count - 1);
    }

    public void e_deleteTake(int index) {
		string prevDefaultTakeName = defaultTakeName;
        //if(shouldCheckDependencies) shouldCheckDependencies = false;

		//TODO: destroy tracks, keys
		//_takes[index].destroy();
		_takes.RemoveAt(index);
        if((e_currentTake >= index) && (e_currentTake > 0)) e_currentTake--;

		if(!string.IsNullOrEmpty(prevDefaultTakeName)) {
			string newPlayOnStart = "";
			foreach(AMTakeData take in _takes) {
				if(take.name == prevDefaultTakeName) {
					newPlayOnStart = take.name;
					break;
				}
			}
			
			defaultTakeName = newPlayOnStart;
		}
    }

    public void e_selectTake(int index) {
        if(e_currentTake != index)
            e_prevTake = e_currentTake;

        e_currentTake = index;
    }

    public void e_selectTake(string name) {
		List<AMTakeData> _ts = _takes;
		for(int i = 0; i < _ts.Count; i++) {
			if(_ts[i].name == name) {
                e_selectTake(i);
                break;
            }
		}
    }
    public void e_makeTakeNameUnique(AMTakeData take) {
        bool loop = false;
        int count = 0;
        do {
            if(loop) loop = false;
			foreach(AMTakeData _take in _takes) {
                if(_take != take && _take.name == take.name) {
                    if(count > 0) take.name = take.name.Substring(0, take.name.Length - 3);
                    count++;
                    take.name += "(" + count + ")";
                    loop = true;
                    break;
                }
            }
        } while(loop);
    }

    public string[] e_getTakeNames() {
		List<AMTakeData> _ts = _takes;
		string[] names = new string[_ts.Count + 2];
		for(int i = 0; i < _ts.Count; i++) {
			names[i] = _ts[i].name;
        }
        names[names.Length - 2] = "Create new...";
        names[names.Length - 1] = "Duplicate current...";
        return names;
    }
	    
    public bool e_setCodeLanguage(int codeLanguage) {
        if(this.codeLanguage != codeLanguage) {
            this.codeLanguage = codeLanguage;
            return true;
        }
        return false;
    }
    /*public bool setShowWarningForLostReferences(bool showWarningForLostReferences) {
        if(this.showWarningForLostReferences != showWarningForLostReferences) {
            this.showWarningForLostReferences = showWarningForLostReferences;
            return true;
        }
        return false;
    }*/

    public void e_deleteAllTakesExcept(AMTakeData take) {
		List<AMTakeData> _ts = _takes;
		for(int index = 0; index < _ts.Count; index++) {
			if(_ts[index] == take) continue;
            e_deleteTake(index);
            index--;
        }
    }

    public void e_mergeWith(AnimatorData _aData) {
		if(meta == null && _aData.meta == null) {
			foreach(AMTakeData take in _aData._takes) {
				_takes.Add(take);
	            e_makeTakeNameUnique(take);
	        }
		}
    }

    public List<GameObject> e_getDependencies(AMTakeData _take = null) {
        // if only one take
        if(_take != null) return _take.getDependencies(this).ToList();

        // if all takes
        List<GameObject> ls = new List<GameObject>();
        foreach(AMTakeData take in _takes) {
            ls = ls.Union(take.getDependencies(this)).ToList();
        }
        return ls;
    }

    public List<GameObject> e_updateDependencies(List<GameObject> newReferences, List<GameObject> oldReferences) {
        List<GameObject> lsFlagToKeep = new List<GameObject>();
		foreach(AMTakeData take in _takes) {
            lsFlagToKeep = lsFlagToKeep.Union(take.updateDependencies(this, newReferences, oldReferences)).ToList();
        }
        return lsFlagToKeep;
    }
#endif
#endregion
}
