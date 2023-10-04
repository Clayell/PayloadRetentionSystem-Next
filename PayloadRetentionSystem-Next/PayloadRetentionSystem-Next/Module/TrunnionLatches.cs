﻿using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using DockingFunctions;

namespace PayloadRetentionSystemNext.Module
{
	// FEHLER, wir arbeiten bei den Events nie mit "OnCheckCondition" sondern lösen alle manuell aus... kann man sich fragen, ob das gut ist, aber so lange der Event nur von einem Zustand her kommen kann, spielt das wie keine Rolle

	public class ModuleTrunnionLatches : PartModule, IDockable, IModuleInfo
	{
		// Settings

		[KSPField(isPersistant = false), SerializeField]
		public string nodeTransformName = "TrunnionPortNode";

		[KSPField(isPersistant = false), SerializeField]
		public string referenceAttachNode = "TrunnionPortNode"; // if something is connected to this node, then the state is "Attached" (or "Pre-Attached" -> connected in the VAB/SPH)

		[KSPField(isPersistant = false), SerializeField]
		public string controlTransformName = "";

		[KSPField(isPersistant = false), SerializeField]
		public Vector3 dockingOrientation = Vector3.right; // defines the direction of the docking port (when docked at a 0° angle, these local vectors of two ports point into the same direction)
	// FEHLER unnütz hier drin, weil das durch den Node klar ist

		[KSPField(isPersistant = false), SerializeField]
		public int snapCount = 2;


		[KSPField(isPersistant = false), SerializeField]
		public float detectionDistance = 5f;

		[KSPField(isPersistant = false), SerializeField]
		public float approachingDistance = 0.3f;

		[KSPField(isPersistant = false), SerializeField]
		public float captureDistance = 0.03f;


		[KSPField(isPersistant = false)]
		public bool gendered = true;

		[KSPField(isPersistant = false)]
		public bool genderFemale = false;

		[KSPField(isPersistant = false)]
		public string nodeType = "Trunnion";

		[KSPField(isPersistant = false)]
		public float breakingForce = 10f;

		[KSPField(isPersistant = false)]
		public float breakingTorque = 10f;

		[KSPField(isPersistant = false)]
		public string nodeName = "";				// FEHLER, mal sehen wozu wir den dann nutzen könnten


		// Docking and Status

		public Transform nodeTransform;
		public Transform controlTransform;	// FEHLER, klären, was die hier sollen? allgemein überall... echt jetzt

//		public Transform portTransform; // FEHLER, neue Idee -> und, wozu sind die anderen da oben eigentlich gut? -> das bei allen Ports mal vereinheitlichen
			// FEHLER, ist genau gleich dem nodeTransform -> daher nur den nutzen

		public KerbalFSM fsm;

		public KFSMState st_active;			// "active" / "searching"

		public KFSMState st_approaching;	// port found

		public KFSMState st_latching;		// orienting and retracting in progress
		public KFSMState st_prelatched;		// ready to dock
		public KFSMState st_latched;		// docked

		public KFSMState st_unlatching;		// opening latches

		public KFSMState st_docked;			// docked or docked_to_same_vessel
		public KFSMState st_preattached;

		public KFSMState st_disabled;


		public KFSMEvent on_approach;
		public KFSMEvent on_distance;

		public KFSMEvent on_latching;
		public KFSMEvent on_prelatch;
		public KFSMEvent on_latch;

		public KFSMEvent on_unlatching;

		public KFSMEvent on_release;

		public KFSMEvent on_dock;
		public KFSMEvent on_undock;

		public KFSMEvent on_enable;
		public KFSMEvent on_disable;

		// Sounds

/* FEHLER, Sound fehlt noch total -> ah und einige Servos spielen keinen Sound, was ist da falsch? -> hat nix mit LEE zu tun zwar

		[KSPField(isPersistant = false)] public string preAttachSoundFilePath = "";
		[KSPField(isPersistant = false)] public string latchSoundFilePath = "";
		[KSPField(isPersistant = false)] public string detachSoundFilePath = "";
		
		[KSPField(isPersistant = false)] public string activatingSoundFilePath = "";
		[KSPField(isPersistant = false)] public string activatedSoundFilePath = "";
		[KSPField(isPersistant = false)] public string deactivatingSoundFilePath = "";

		protected SoundSource soundSound = null;
*/

		// Capturing / Docking

		public ModuleTrunnionPins otherPort;
		public uint dockedPartUId;

		public DockedVesselInfo vesselInfo;
		public bool docked = false; // true, if the vessel of the otherPort is and should be the same as our vessel

		private bool inCaptureDistance = false;

		private ConfigurableJoint CaptureJoint;

		private Quaternion CaptureJointTargetRotation;
		private Vector3 CaptureJointTargetPosition;

		private Vector3 CaptureJointWoherIchKomme;	// FEHLER, alles Müll hier

		private float _rotStep;
		float _transstep = 0.0005f;
		int iPos = 0;

		// Packed / OnRails

		private int followOtherPort = 0;

		private Vector3 otherPortRelativePosition;
		private Quaternion otherPortRelativeRotation;

		////////////////////////////////////////
		// Constructor

		public ModuleTrunnionLatches()
		{
		}

		////////////////////////////////////////
		// Callbacks

		public override void OnAwake()
		{
		//	part.dockingPorts.AddUnique(this);
		}

		public override void OnLoad(ConfigNode node)
		{
			base.OnLoad(node);

		//	if(node.HasValue("length"))
		//		length = float.Parse(node.GetValue("length"));

			if(node.HasValue("state"))
				DockStatus = node.GetValue("state");
			else
				DockStatus = "Inactive";

			if(node.HasValue("dockUId"))
				dockedPartUId = uint.Parse(node.GetValue("dockUId"));

			if(node.HasValue("docked"))
				docked = bool.Parse(node.GetValue("docked"));

			if(node.HasNode("DOCKEDVESSEL"))
			{
				vesselInfo = new DockedVesselInfo();
				vesselInfo.Load(node.GetNode("DOCKEDVESSEL"));
			}

// FEHLER, hier fehlt noch Zeugs

			if(node.HasValue("followOtherPort"))
			{
				followOtherPort = int.Parse(node.GetValue("followOtherPort"));

				node.TryGetValue("otherPortRelativePosition", ref otherPortRelativePosition);
				node.TryGetValue("otherPortRelativeRotation", ref otherPortRelativeRotation);
			}
		}

		public override void OnSave(ConfigNode node)
		{
			base.OnSave(node);

		//	node.AddValue("length", length);

			node.AddValue("state", (string)(((fsm != null) && (fsm.Started)) ? fsm.currentStateName : DockStatus));

			node.AddValue("dockUId", dockedPartUId);

			node.AddValue("docked", docked);

			if(vesselInfo != null)
				vesselInfo.Save(node.AddNode("DOCKEDVESSEL"));

// FEHLER, hier fehlt noch Zeugs

			node.AddValue("followOtherPort", followOtherPort);

			if(followOtherPort != 0)
			{
				if(otherPortRelativePosition != null)	node.AddValue("otherPortRelativePosition", otherPortRelativePosition);
				if(otherPortRelativeRotation != null)	node.AddValue("otherPortRelativeRotation", otherPortRelativeRotation);
			}
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			Fields["length"].OnValueModified += onChanged_length;
			onChanged_length(null);

			Fields["width"].OnValueModified += onChanged_width;
			onChanged_width(null);

			if(state == StartState.Editor)
				return;

			GameEvents.onVesselGoOnRails.Add(OnPack);
			GameEvents.onVesselGoOffRails.Add(OnUnpack);

		//	GameEvents.onFloatingOriginShift.Add(OnFloatingOriginShift);

			nodeTransform = base.part.FindModelTransform(nodeTransformName);
			if(!nodeTransform)
			{
				Debug.LogWarning("[Docking Node Module]: WARNING - No node transform found with name " + nodeTransformName, base.part.gameObject);
				return;
			}
			if(controlTransformName == string.Empty)
				controlTransform = base.part.transform;
			else
			{
				controlTransform = base.part.FindModelTransform(controlTransformName);
				if(!controlTransform)
				{
					Debug.LogWarning("[Docking Node Module]: WARNING - No control transform found with name " + controlTransformName, base.part.gameObject);
					controlTransform = base.part.transform;
				}
			}

//			portTransform = part.FindAttachNode("TrunnionPortNode").nodeTransform;

			StartCoroutine(WaitAndInitialize(state));

			StartCoroutine(WaitAndInitializeDockingNodeFix());
		}

		// FEHLER, ist 'n Quickfix, solange der blöde Port noch drüber hängt im Part...
		public IEnumerator WaitAndInitializeDockingNodeFix()
		{
			ModuleDockingNode DockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if(DockingNode)
			{
				while((DockingNode.fsm == null) || (!DockingNode.fsm.Started))
					yield return null;

				DockingNode.fsm.RunEvent(DockingNode.on_disable);
			}
		}

		public IEnumerator WaitAndInitialize(StartState st)
		{
			yield return null;

			Events["TogglePort"].active = false;

			Events["Latch"].active = false;
			Events["Release"].active = false;

			Events["Dock"].active = false;
			Events["Undock"].active = false;

			if(dockedPartUId != 0)
			{
				Part otherPart;

				while(!(otherPart = FlightGlobals.FindPartByID(dockedPartUId)))
					yield return null;

				otherPort = otherPart.GetComponent<ModuleTrunnionPins>();

		// FEHLER, logo, das könnte auch er laden... aber... na ja...
				otherPort.otherPort = this;
				otherPort.dockedPartUId = part.flightID;
			}

			if((DockStatus == "Inactive")
			|| ((DockStatus == "Attached") && (otherPort == null))) // fix damaged state (just in case)
			{
// FEHLER, Frage: warum kann das passieren, dass hier "Attached" und null ist? hmm? woher kommt das?

				// fix state if attached to other port

				if(referenceAttachNode != string.Empty)
				{
					AttachNode node = part.FindAttachNode(referenceAttachNode);
					if((node != null) && node.attachedPart)
					{
						ModuleTrunnionPins _otherPort = node.attachedPart.GetComponent<ModuleTrunnionPins>();

						if(_otherPort)
						{
							otherPort = _otherPort;
							dockedPartUId = otherPort.part.flightID;

							DockStatus = "Attached";
							otherPort.DockStatus = "Attached";
						}
					}
				}
			}

// FEHLER wenn ich nicht disabled bin, dann meinen GF disablen... so oder so... -> und das dort auch noch reinnehmen in die st_^'s

			SetupFSM();

			if((DockStatus == "Approaching")
			|| (DockStatus == "Latching")		// not required
			|| (DockStatus == "Pre Latched")	// not required
			|| (DockStatus == "Latched")
			|| (DockStatus == "Released"))
			{
// FEHLER
//nope, ich muss alles bauen von dem Teil da... gut, auf den fsm von ihm kann ich zwar warten... das stimmt wohl
//ok, ansehen, dass wir's koordinieren

				if(otherPort != null)
				{
					while(!otherPort.part.started || (otherPort.fsm == null) || (!otherPort.fsm.Started))
						yield return null;
				}
			}

	// FEHLER, das hier noch weiter verfeinern, die Zustände und so...
// tun wir hier, weil die Teilers gestartet sein müssen, damit's nicht schief geht beim connect und so -> Rigidbody existiert sonst noch nicht uns so...
			if(DockStatus == "Latched")
			{
				BuildCaptureJoint(otherPort);
				BuildCaptureJoint2();
			}

			if(DockStatus == "Docked")
			{
				if(vessel == otherPort.vessel)
					docked = true;

				otherPort.DockStatus = "Docked";

// FEHLER, erster Murks
DockingHelper.OnLoad(this, vesselInfo, otherPort, otherPort.vesselInfo);
			}

			fsm.StartFSM(DockStatus);
		}

		public void OnDestroy()
		{
			Fields["length"].OnValueModified -= onChanged_length;
			Fields["width"].OnValueModified -= onChanged_width;

			GameEvents.onVesselGoOnRails.Remove(OnPack);
			GameEvents.onVesselGoOffRails.Remove(OnUnpack);

		//	GameEvents.onFloatingOriginShift.Remove(OnFloatingOriginShift);
		}

		private void OnPack(Vessel v)
		{
			if(vessel == v)
			{
				if(DockStatus == "Latched")
				{
					if(Vessel.GetDominantVessel(vessel, otherPort.vessel) == otherPort.vessel)
					{
						followOtherPort = 1;
						VesselPositionManager.Register(part, otherPort.part, true, out otherPortRelativePosition, out otherPortRelativeRotation);
					}
					else
					{
						followOtherPort = 2;
						VesselPositionManager.Register(otherPort.part, part, true, out otherPortRelativePosition, out otherPortRelativeRotation);
					}
				}
			}
		}

		private void OnUnpack(Vessel v)
		{
			if(vessel == v)
			{
				if(DockStatus == "Latched")
				{
					VesselPositionManager.Unregister((followOtherPort == 1) ? vessel : otherPort.vessel);
					followOtherPort = 0;
				}

		//		StartCoroutine(OnUnpackDelayed());
			}
		}

		////////////////////////////////////////
		// Functions
/*
		void SetCollisions(Collider[] a, Collider[] b, bool ignore)
		{
			for(int i = 0; i < a.Length; i++)
			{
				for(int j = 0; j < b.Length; j++)
				{
					Collider collider = a[i];
					Collider collider2 = b[j];
					if(!(collider.attachedRigidbody == collider2.attachedRigidbody))
					{
						Physics.IgnoreCollision(collider, collider2, ignore);

Vector3 v; float d;
Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, collider2, collider2.transform.position, collider2.transform.rotation, out v, out d);
					}
				}
			}

			Physics.SyncTransforms();
		}

static bool fuckParent = false;
*/
static float baseForce = 1000f;

		public void SetupFSM()
		{
			fsm = new KerbalFSM();

			st_active = new KFSMState("Ready");
			st_active.OnEnter = delegate(KFSMState from)
			{
				otherPort = null;
				dockedPartUId = 0;

				Events["TogglePort"].guiName = "Deactivate End Effector";
				Events["TogglePort"].active = true;
			};
			st_active.OnFixedUpdate = delegate
			{
				Vector3 distance; float angle;

				for(int i = 0; i < FlightGlobals.VesselsLoaded.Count; i++)
				{
					Vessel vessel = FlightGlobals.VesselsLoaded[i];

					if(vessel.packed
						/*|| (vessel == part.vessel)*/) // no docking to ourself is possible
						continue;

					for(int j = 0; j < vessel.dockingPorts.Count; j++)
					{
						PartModule partModule = vessel.dockingPorts[j];

						if((partModule.part == null)
						/*|| (partModule.part == part)*/ // no docking to ourself is possible
						|| (partModule.part.State == PartStates.DEAD))
							continue;

						ModuleTrunnionPins _otherPort = partModule.GetComponent<ModuleTrunnionPins>();

						if(_otherPort == null)
							continue;

						if(_otherPort.fsm.CurrentState != _otherPort.st_passive)
							continue;

						distance = _otherPort.nodeTransform.position - nodeTransform.position;

						if(distance.magnitude < detectionDistance)
						{
							angle = Vector3.Angle(nodeTransform.forward, -_otherPort.nodeTransform.forward);

							DockDistance = distance.magnitude.ToString();
							DockAngle = "-";

							if((angle <= 15f) && (distance.magnitude <= approachingDistance))
							{
								otherPort = _otherPort;
								dockedPartUId = otherPort.part.flightID;

								fsm.RunEvent(on_approach);
								otherPort.fsm.RunEvent(otherPort.on_approach_passive);
								return;
							}
						}
					}
				}

				DockDistance = "-";
				DockAngle = "-";
			};
			st_active.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_active);

			st_approaching = new KFSMState("Approaching");
			st_approaching.OnEnter = delegate(KFSMState from)
			{
				Events["TogglePort"].active = false;

				inCaptureDistance = false;

				otherPort.otherPort = this;
				otherPort.dockedPartUId = part.flightID;
			};
			st_approaching.OnFixedUpdate = delegate
			{
				Vector3 distance = otherPort.nodeTransform.position - nodeTransform.position;

				DockDistance = distance.magnitude.ToString();

				if(distance.magnitude < captureDistance)
				{
					Vector3 tvref = nodeTransform.TransformDirection(dockingOrientation);
					Vector3 tv = otherPort.nodeTransform.TransformDirection(otherPort.dockingOrientation);
//					Vector3 tvref = nodeTransform.right;
//					Vector3 tv = otherPort.nodeTransform.right;
					float ang = Vector3.SignedAngle(tvref, tv, -nodeTransform.forward);

					ang = 360f + ang - (180f / snapCount);
					ang %= (360f / snapCount);
					ang -= (180f / snapCount);

					bool angleok = ((ang > -5f) && (ang < 5f));

					DockAngle = ang.ToString();

					if(angleok)
					{
						if(!inCaptureDistance)
							Events["Latch"].active = true;

						inCaptureDistance = true;

						return;
					}
				}
				else
					DockAngle = "-";

				if(inCaptureDistance)
					Events["Latch"].active = false;

				inCaptureDistance = false;
				
				if(distance.magnitude < 1.5f * approachingDistance)
				{
					float angle = Vector3.Angle(nodeTransform.forward, -otherPort.nodeTransform.forward);

					if(angle <= 15f)
						return;
				}

				otherPort.fsm.RunEvent(otherPort.on_distance_passive);
				fsm.RunEvent(on_distance);
			};
			st_approaching.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_approaching);

			st_latching = new KFSMState("Latching");
			st_latching.OnEnter = delegate(KFSMState from)
			{
				Events["Latch"].active = false;
				Events["Release"].active = true;

				BuildCaptureJoint(otherPort);
				BuildCaptureJoint2();

				_transstep = 0.0005f / (nodeTransform.position - otherPort.nodeTransform.position).magnitude;

				CaptureJointWoherIchKomme = CaptureJoint.targetPosition;

				part.GetComponent<ModuleAnimateGeneric>().Toggle();
			};
			st_latching.OnFixedUpdate = delegate
			{
				if(part.GetComponent<ModuleAnimateGeneric>().Progress == 1f)
				{
					CaptureJoint.targetRotation = CaptureJointTargetRotation;
					CaptureJoint.targetPosition = CaptureJointTargetPosition;

					fsm.RunEvent(on_prelatch);
				}
				else
				{
				//	_rotStep -= _transstep; -> FEHLER, war früher so, aber für diesen Port ergibt das keinen Sinn, weil das sowieso zu schnell laufen würde
					_rotStep = part.GetComponent<ModuleAnimateGeneric>().Progress;

					CaptureJoint.targetRotation = Quaternion.Slerp(CaptureJointTargetRotation, Quaternion.identity, _rotStep);

					Vector3 diff = otherPort.nodeTransform.position - nodeTransform.position;
					diff = CaptureJoint.transform.InverseTransformDirection(diff);

					if(diff.magnitude < 0.0005f)
						CaptureJoint.targetPosition -= diff;
					else
						CaptureJoint.targetPosition -= diff.normalized * 0.0005f;
	// FEHLER, etwas unschön, weil ich kein Slerp machen kann, weil ich mich vorher ausgerichtet habe... hmm... -> evtl. Basis rechnen, dann differenz davon und dann... dazwischen Slerpen?

// FEHLER, hab's doch noch neu gemacht... mal sehen ob's so stimmt oder zumindest etwas besser passt
CaptureJoint.targetPosition = Vector3.Slerp(CaptureJointTargetPosition, CaptureJointWoherIchKomme, _rotStep);
				}
			};
			st_latching.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_latching);

			st_prelatched = new KFSMState("Pre Latched");
			st_prelatched.OnEnter = delegate(KFSMState from)
			{
	//			Events["Release"].active = true;

				iPos = 10;

	//			if(part.GetComponent<ModuleAnimateGeneric>().Progress > 0f)
	//			part.GetComponent<ModuleAnimateGeneric>().Toggle();
			};
			st_prelatched.OnFixedUpdate = delegate
			{
				if(--iPos < 0)
				{
					Events["Release"].active = true;

					fsm.RunEvent(on_latch);
					otherPort.fsm.RunEvent(otherPort.on_latch_passive);
				}
			};
			st_prelatched.OnLeave = delegate(KFSMState to)
			{
// FEHLER, evtl. noch relaxing machen, wenn gleiches Schiff?

/*
				DockToVessel(otherPort);

				Destroy(CaptureJoint);
				CaptureJoint = null;
*/
		//		otherPort.fsm.RunEvent(otherPort.on_dock);
			};
			fsm.AddState(st_prelatched);
		
			st_latched = new KFSMState("Latched");
			st_latched.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = true;

				Events["Dock"].active = true;
				Events["Undock"].active = false;

				JointDrive angularDrive = new JointDrive { maximumForce = PhysicsGlobals.JointForce, positionSpring = 60000f, positionDamper = 0f };
				CaptureJoint.angularXDrive = CaptureJoint.angularYZDrive = CaptureJoint.slerpDrive = angularDrive;

				JointDrive linearDrive = new JointDrive { maximumForce = PhysicsGlobals.JointForce, positionSpring = PhysicsGlobals.JointForce, positionDamper = 0f };
				CaptureJoint.xDrive = CaptureJoint.yDrive = CaptureJoint.zDrive = linearDrive;
			};
			st_latched.OnFixedUpdate = delegate
			{
			};
			st_latched.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_latched);


			st_unlatching = new KFSMState("Unlatching");
			st_unlatching.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = false;
				Events["Latch"].active = false;
				Events["Dock"].active = false;

				part.GetComponent<ModuleAnimateGeneric>().Toggle();
			};
			st_unlatching.OnFixedUpdate = delegate
			{
				if(part.GetComponent<ModuleAnimateGeneric>().Progress == 1f)
				{
					DestroyCaptureJoint();

					if(otherPort != null)
						otherPort.fsm.RunEvent(otherPort.on_release_passive);

					fsm.RunEvent(on_release);
				}
				else
				{
// FEHLER, ich probier mal was...

			JointDrive drive =
				new JointDrive
				{
					positionSpring = (1f - part.GetComponent<ModuleAnimateGeneric>().Progress) * baseForce,
					positionDamper = 0f,
					maximumForce = PhysicsGlobals.JointForce
				};

					CaptureJoint.xDrive = CaptureJoint.yDrive = CaptureJoint.zDrive = drive;
				}
			};
			st_unlatching.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_unlatching);


			st_docked = new KFSMState("Docked");
			st_docked.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = false;

				Events["Dock"].active = false;
				Events["Undock"].active = true;
			};
			st_docked.OnFixedUpdate = delegate
			{
			};
			st_docked.OnLeave = delegate(KFSMState to)
			{
				Events["Undock"].active = false;
			};
			fsm.AddState(st_docked);

			st_preattached = new KFSMState("Attached");
			st_preattached.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = false;

				Events["Undock"].active = true;
			};
			st_preattached.OnFixedUpdate = delegate
			{
			};
			st_preattached.OnLeave = delegate(KFSMState to)
			{
				Events["Undock"].active = false;
			};
			fsm.AddState(st_preattached);

			st_disabled = new KFSMState("Inactive");
			st_disabled.OnEnter = delegate(KFSMState from)
			{
				Events["TogglePort"].guiName = "Activate End Effector";
				Events["TogglePort"].active = true;
			};
			st_disabled.OnFixedUpdate = delegate
			{
			};
			st_disabled.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_disabled);


			on_approach = new KFSMEvent("Approaching");
			on_approach.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_approach.GoToStateOnEvent = st_approaching;
			fsm.AddEvent(on_approach, st_active);

			on_distance = new KFSMEvent("Distancing");
			on_distance.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_distance.GoToStateOnEvent = st_active;
			fsm.AddEvent(on_distance, st_approaching, st_docked, st_preattached);

			on_latching = new KFSMEvent("Latch");
			on_latching.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_latching.GoToStateOnEvent = st_latching;
			fsm.AddEvent(on_latching, st_approaching);

			on_prelatch = new KFSMEvent("Pre Latch");
			on_prelatch.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_prelatch.GoToStateOnEvent = st_prelatched;
			fsm.AddEvent(on_prelatch, st_latching);

			on_latch = new KFSMEvent("Latched");
			on_latch.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_latch.GoToStateOnEvent = st_latched;
			fsm.AddEvent(on_latch, st_prelatched);


			on_unlatching = new KFSMEvent("Unlatch");
			on_unlatching.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_unlatching.GoToStateOnEvent = st_unlatching;
			fsm.AddEvent(on_unlatching, st_latched);


			on_release = new KFSMEvent("Released");
			on_release.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_release.GoToStateOnEvent = st_active;
			fsm.AddEvent(on_release, st_unlatching);


			on_dock = new KFSMEvent("Perform docking");
			on_dock.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_dock.GoToStateOnEvent = st_docked;
			fsm.AddEvent(on_dock, st_latched);

			on_undock = new KFSMEvent("Undock");
			on_undock.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_undock.GoToStateOnEvent = st_latched;
			fsm.AddEvent(on_undock, st_docked, st_preattached);


			on_enable = new KFSMEvent("Enable");
			on_enable.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_enable.GoToStateOnEvent = st_active;
			fsm.AddEvent(on_enable, st_disabled);

			on_disable = new KFSMEvent("Disable");
			on_disable.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_disable.GoToStateOnEvent = st_disabled;
			fsm.AddEvent(on_disable, st_active);
		}

		// calculate position and orientation for st_capture
		void CalculateCaptureJointRotationAndPosition(ModuleTrunnionPins port, out Quaternion rotation, out Vector3 position)
		{
			Vector3 tvref =
				transform.InverseTransformDirection(nodeTransform.TransformDirection(dockingOrientation));

			Vector3 portDockingOrientation = port.nodeTransform.TransformDirection(port.dockingOrientation);
			Vector3 tv = transform.InverseTransformDirection(portDockingOrientation);

			float angle = 0f;

			Vector3.SignedAngle(
				nodeTransform.forward, port.nodeTransform.forward,
				nodeTransform.up);

			for(int i = 1; i < snapCount; i++)
			{
				float ff = (360f / snapCount) * i;

				Vector3 tv2 = transform.InverseTransformDirection(Quaternion.AngleAxis(ff, port.nodeTransform.forward) * portDockingOrientation);

				if(Vector3.Angle(tv, tvref) > Vector3.Angle(tv2, tvref))
				{
					tv = tv2;
					angle = ff;
				}
			}

//			angle /= (360f / snapCount);
//			angle = Mathf.Round(angle);

//			angle = ((int)angle % snapCount) * (360f / snapCount);

			Quaternion qt = Quaternion.LookRotation(transform.InverseTransformDirection(nodeTransform.forward), transform.InverseTransformDirection(nodeTransform.TransformDirection(dockingOrientation)));
			Quaternion qc = Quaternion.LookRotation(transform.InverseTransformDirection(-port.nodeTransform.forward), tv);
//			Quaternion qt = Quaternion.Inverse(transform.rotation) * portTransform.rotation;
//			Quaternion qc = Quaternion.AngleAxis(
//				angle,
//				Quaternion.Inverse(transform.rotation) * portTransform.up);

			rotation = qt * Quaternion.Inverse(qc);


			Vector3 diff = port.nodeTransform.position - nodeTransform.position;
		//	Vector3 difflp = Vector3.ProjectOnPlane(diff, transform.forward);

			position = -transform.InverseTransformDirection(diff);
		}

		private void BuildCaptureJoint(ModuleTrunnionPins port)
		{
		// FEHLER, müsste doch schon gesetzt sein... aber gut...
			otherPort = port;
			dockedPartUId = otherPort.part.flightID;

			otherPort.otherPort = this;
			otherPort.dockedPartUId = part.flightID;

			// Joint
			ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();

			joint.connectedBody = otherPort.part.Rigidbody;
joint.enableCollision = true; // FEHLER, hier brauch ich das wohl ... -> klären, wo das noch sein müsste vielleicht??

			joint.breakForce = joint.breakTorque = Mathf.Infinity;
// FEHLER FEHLER -> breakForce min von beiden und torque auch

			// we calculate with the "stack" force -> thus * 4f and not * 1.6f

			float breakingForceModifier = 1f;
			float breakingTorqueModifier = 1f;

			float defaultLinearForce = Mathf.Min(part.breakingForce, otherPort.part.breakingForce) *
				breakingForceModifier * 4f;

			float defaultTorqueForce = Mathf.Min(part.breakingTorque, otherPort.part.breakingTorque) *
				breakingTorqueModifier * 4f;

			joint.breakForce = defaultLinearForce;
			joint.breakTorque = defaultTorqueForce;


			joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
			joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;

			JointDrive drive =
				new JointDrive
				{
					positionSpring = 100f,
					positionDamper = 0f,
					maximumForce = 100f
				};

			joint.angularXDrive = joint.angularYZDrive = joint.slerpDrive = drive;
			joint.xDrive = joint.yDrive = joint.zDrive = drive;

			CaptureJoint = joint;

			DockDistance = "-";
			DockAngle = "-";
		}

		private void BuildCaptureJoint2()
		{
			CalculateCaptureJointRotationAndPosition(otherPort, out CaptureJointTargetRotation, out CaptureJointTargetPosition);
			_rotStep = 1f;
		}

		private void DestroyCaptureJoint()
		{
			// Joint
			Destroy(CaptureJoint);
			CaptureJoint = null;

			// FEHLER, nur mal so 'ne Idee... weiss nicht ob das gut sit

			vessel.ResetRBAnchor();
			if(otherPort) otherPort.vessel.ResetRBAnchor();
		}

		////////////////////////////////////////
		// Update-Functions

		public void FixedUpdate()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				if(vessel && !vessel.packed)
				{

				if((fsm != null) && fsm.Started)
					fsm.FixedUpdateFSM();

				}
/*
				if(vessel.packed && followOtherPort)
				{
					vessel.SetRotation(otherPort.part.transform.rotation * otherPortRelativeRotation, true);
					vessel.SetPosition(otherPort.part.transform.position + otherPort.part.transform.rotation * otherPortRelativePosition, false);
				//	vessel.IgnoreGForces(5);
				}
*/
			}
		}

		public void Update()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				if(vessel && !vessel.packed)
				{

				if((fsm != null) && fsm.Started)
				{
					fsm.UpdateFSM();
					DockStatus = fsm.currentStateName;
				}

				}
			}
		}

		public void LateUpdate()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				if(vessel && !vessel.packed)
				{

				if((fsm != null) && fsm.Started)
					fsm.LateUpdateFSM();
				}
			}
		}

		////////////////////////////////////////
		// Settings

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Length", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = 0.6f, maxValue = 8f),
			UI_FloatRange(minValue = 0.6f, maxValue = 8f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float length = 2f;

		private void onChanged_length(object o)
		{
			Transform Base000 = KSPUtil.FindInPartModel(transform, "Base.000");
			Transform Base001 = KSPUtil.FindInPartModel(transform, "Base.001");
			Transform Base002 = KSPUtil.FindInPartModel(transform, "Base.002");
			Transform Base003 = KSPUtil.FindInPartModel(transform, "Base.003");

			Vector3 Pos000 = Base000.localPosition;
			Pos000.x = length * 0.5f;
			Base000.localPosition = Pos000;

			Vector3 Pos001 = Base001.localPosition;
			Pos001.x = -length * 0.5f;
			Base001.localPosition = Pos001;

			Vector3 Pos002 = Base002.localPosition;
			Pos002.x = length * 0.5f;
			Base002.localPosition = Pos002;

			Vector3 Pos003 = Base003.localPosition;
			Pos003.x = -length * 0.5f;
			Base003.localPosition = Pos003;
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Width", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = -0.6f, maxValue = 0.6f),
			UI_FloatRange(minValue = -0.6f, maxValue = 0.6f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float width = 0f;

		private void onChanged_width(object o)
		{
			Transform Base000 = KSPUtil.FindInPartModel(transform, "Base.000");
			Transform Base001 = KSPUtil.FindInPartModel(transform, "Base.001");
			Transform Base002 = KSPUtil.FindInPartModel(transform, "Base.002");
			Transform Base003 = KSPUtil.FindInPartModel(transform, "Base.003");

			Vector3 Pos000 = Base000.localPosition;
			Pos000.z = -1.334f - width * 0.5f;
			Base000.localPosition = Pos000;

			Vector3 Pos001 = Base001.localPosition;
			Pos001.z = -1.334f - width * 0.5f;
			Base001.localPosition = Pos001;

			Vector3 Pos002 = Base002.localPosition;
			Pos002.z = 1.334f + width * 0.5f;
			Base002.localPosition = Pos002;

			Vector3 Pos003 = Base003.localPosition;
			Pos003.z = 1.334f + width * 0.5f;
			Base003.localPosition = Pos003;
		}

		////////////////////////////////////////
		// Context Menu

		[KSPField(guiName = "Trunnion Port status", isPersistant = false, guiActive = true, guiActiveUnfocused = true, unfocusedRange = 20)]
		public string DockStatus = "Inactive";

		[KSPField(guiName = "Trunnion Port distance", isPersistant = false, guiActive = true)]
		public string DockDistance;

		[KSPField(guiName = "Trunnion Port angle", isPersistant = false, guiActive = true)]
		public string DockAngle;

		public void Enable()
		{
			fsm.RunEvent(on_enable);
		}

		public void Disable()
		{
			fsm.RunEvent(on_disable);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, guiName = "Deactivate End Effector")]
		public void TogglePort()
		{
			if(fsm.CurrentState == st_disabled)
				fsm.RunEvent(on_enable);
			else
				fsm.RunEvent(on_disable);
		}

	// das ist das pull-back und eine Drehung (gleichzeitig)
		[KSPEvent(guiActive = true, guiActiveUnfocused = false, guiName = "Latch")]
		public void Latch()
		{
			fsm.RunEvent(on_latching);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = false, guiName = "Release")]
		public void Release()
		{
			fsm.RunEvent(on_unlatching);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = false, guiName = "Dock")]
		public void Dock()
		{
			DockToVessel(otherPort);

			Destroy(CaptureJoint);
			CaptureJoint = null;

			fsm.RunEvent(on_dock);
			otherPort.fsm.RunEvent(otherPort.on_dock_passive);
		}

		public void DockToVessel(ModuleTrunnionPins port)
		{
			Debug.Log("Docking to vessel " + port.vessel.GetDisplayName(), gameObject);

			otherPort = port;
			dockedPartUId = otherPort.part.flightID;

			otherPort.otherPort = this;
			otherPort.dockedPartUId = part.flightID;

			DockingHelper.SaveCameraPosition(part);
			DockingHelper.SuspendCameraSwitch(10);

			if(otherPort.vessel == Vessel.GetDominantVessel(vessel, otherPort.vessel))
				DockingHelper.DockVessels(this, otherPort);
			else
				DockingHelper.DockVessels(otherPort, this);

			DockingHelper.RestoreCameraPosition(part);
		}

		private void DoUndock()
		{
			DockingHelper.SaveCameraPosition(part);
			DockingHelper.SuspendCameraSwitch(10);

			DockingHelper.UndockVessels(this, otherPort);

			BuildCaptureJoint(otherPort);
			BuildCaptureJoint2();

			DockingHelper.RestoreCameraPosition(part);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 2f, guiName = "#autoLOC_6001445")]
		public void Undock()
		{
			Vessel oldvessel = vessel;
			uint referenceTransformId = vessel.referenceTransformId;

			DoUndock();

			otherPort.fsm.RunEvent(otherPort.on_undock_passive);
			fsm.RunEvent(on_undock);

			if(oldvessel == FlightGlobals.ActiveVessel)
			{
				if(vessel[referenceTransformId] == null)
					StartCoroutine(WaitAndSwitchFocus());
			}
		}

		public IEnumerator WaitAndSwitchFocus()
		{
			yield return null;

			DockingHelper.SaveCameraPosition(part);

			FlightGlobals.ForceSetActiveVessel(vessel);
			FlightInputHandler.SetNeutralControls();

			DockingHelper.RestoreCameraPosition(part);
		}

		////////////////////////////////////////
		// Actions

		[KSPAction("Enable")]
		public void EnableAction(KSPActionParam param)
		{ Enable(); }

		[KSPAction("Disable")]
		public void DisableAction(KSPActionParam param)
		{ Disable(); }

		[KSPAction("Dock", activeEditor = false)]
		public void DockAction(KSPActionParam param)
		{ Dock(); }

		[KSPAction("#autoLOC_6001444", activeEditor = false)]
		public void UndockAction(KSPActionParam param)
		{ Undock(); }

		////////////////////////////////////////
		// IDockable

		private DockInfo dockInfo;

		public Part GetPart()
		{ return part; }

		public Transform GetNodeTransform()
		{ return nodeTransform; }

		public Vector3 GetDockingOrientation()
		{ return dockingOrientation; }

		public int GetSnapCount()
		{ return snapCount; }

		public DockInfo GetDockInfo()
		{ return dockInfo; }

		public void SetDockInfo(DockInfo _dockInfo)
		{
			dockInfo = _dockInfo;
			vesselInfo =
				(dockInfo == null) ? null :
				((dockInfo.part == (IDockable)this) ? dockInfo.vesselInfo : dockInfo.targetVesselInfo);
		}

		public bool IsDocked()
		{
			return ((fsm.CurrentState == st_docked) || (fsm.CurrentState == st_preattached));
		}

		public IDockable GetOtherDockable()
		{
			return IsDocked() ? (IDockable)otherPort : null;
		}

		////////////////////////////////////////
		// IModuleInfo

		string IModuleInfo.GetModuleTitle()
		{
			return "Trunnion Port";
		}

		string IModuleInfo.GetInfo()
		{
/*
			StringBuilder sb = new StringBuilder();
			sb.AppendFormat("Attach strength (catched): {0:F0}\n", catchedBreakForce);
			sb.AppendFormat("Attach strength (latched): {0:F0}\n", latchedBreakForce);

			if(electricChargeRequiredLatching != 0f)
			{
				sb.Append("\n\n");
				sb.Append("<b><color=orange>Requires:</color></b>\n");
				
				if(electricChargeRequiredLatching != 0f)
					sb.AppendFormat("- <b>Electric Charge:</b> {0:F0}\n  (for latching)", electricChargeRequiredLatching);
			}

			return sb.ToString();*/
return ""; // FEHLER, fehlt
		}

		Callback<Rect> IModuleInfo.GetDrawModulePanelCallback()
		{
			return null;
		}

		string IModuleInfo.GetPrimaryField()
		{
			return null;
		}
	}
}
