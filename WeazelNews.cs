/*
 * 
 * Weazel News Job
 * Author: Timothy Dexter
 * Release: 0.0.7
 * Date: 1/02/17
 * 
 * Credits to Mooshe for zoom camera and Weazel News overlay
 * 
 * Known Issues
 * 1) Sometimes decors return null on the job vehicles.  Not sure what causes this yet.
 * 
 * Please send any edits/improvements/bugs to this script back to the author. 
 * 
 * Usage 
 * - /getnewsequip cam|mic to retreive equipment from back of a newsvan
 * - /storenewsequip to store equipment in back of a newsvan
 * - /uploadnewsreport while in a newsvan to upload contents and get paid
 * - w/ microphone equipped:
 *	- Alt + Context (E) to toggle microphone up to player face on air
 *	- Alt + SwitchUnarmed (1 number key) to toggle interview on air
 * - w/ camera equipped:
 *	- Alt + Context (E) to toggle camera on shoulder
 *	- Alt + SwitchUnarmed (1 number key) to live on air overlay
 *	- Alt + SwitchMelee (2 number key) to toggle zoom camera
 * - Player records data when they are on the air with the mic or the cam.
 *		Players can then upload this data from inside their work vehicle
 *		for payment from Weazel news.
 * 
 * History:
 * Revision 0.0.1 2017/12/22 10:27:23 EDT TimothyDexter 
 * - Initial release
 * Revision 0.0.2 2017/12/22 13:32:00 EDT TimothyDexter 
 * - Fixed method looking for parking spot
 * Revision 0.0.3 2017/12/23 16:41:15 EDT TimothyDexter 
 * - Only allow players to return vehicles they rented
 * - Block prone state when interviewing or speaking to camera 
 * - Added help messages for job start and equipment usage
 * - Added command to display equipment help messages
 * - Force clear all animations when equipment is dropped/stored
 * Revision 0.0.4 2017/12/23 19:11:57 EDT TimothyDexter 
 * - Set rental vehicle engine off when spawning to prevent infinite start engine loop on first entry
 * - Added generous check to see if player is recording news footage around other people
 * Revision 0.0.5 2017/12/26 9:28:07 EDT TimothyDexter 
 * - Zoom camera is now attached to actual recording camera
 * Revision 0.0.6 2017/12/26 16:23:28 EDT TimothyDexter 
 * - EmploymentTask uses DateTime to calculate 1s instead of frames
 * Revision 0.0.7 2017/1/02 17:59:43 EDT TimothyDexter 
 * - Added menu items for Weazel News Job
 * - Added gate to check van storage async
 * 
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using CitizenFX.Core.UI;
using Common;
using Roleplay.Client.Classes.Actions;
using Roleplay.Client.Classes.Environment;
using Roleplay.Client.Classes.Environment.UI;
using Roleplay.Client.Classes.Jobs.Police;
using Roleplay.Client.Classes.Player;
using Roleplay.Client.Helpers;
using Roleplay.Enums.Character;
using Roleplay.Enums.Controls;
using Roleplay.Enums.Police;
using Roleplay.SharedClasses;
using Roleplay.SharedModels;
using Newtonsoft.Json;

namespace Roleplay.Client.Classes.Jobs
{
	internal class WeazelNews
	{
		public enum NewsEquipment
		{
			Microphone,
			NewsCamera
		}

		private const int JobEquipmentSecurityDepositFee = 500;
		private const int MaxRecordingTimeMin = 60;

		private static readonly Vector4[] WeazelNewsTruckSpawnSpaces = {
			new Vector4( -529.419f, -899.066f, 23.863f, 60f ),
			new Vector4( -530.410f, -903.225f, 23.862f, 60f ),
			new Vector4( -536.770f, -905.451f, 23.864f, 60f ),
			new Vector4( -538.860f, -909.273f, 23.862f, 60f ),
			new Vector4( -541.078f, -912.640f, 23.862f, 60f ),
			new Vector4( -543.370f, -915.576f, 23.862f, 60f )
		};

		private static Random _rand;
		private static DateTime _onAirTickTime;

		private static readonly Vector3 JobStartLocation = new Vector3( -589.898f, -910.753f, 23.874f - 0.9f );
		private static readonly Vector3 VehicleRentalLocation = new Vector3( -562.711f, -888.325f, 25.166f - 0.9f );
		private static readonly Vector3 RentalVehicleReturn = new Vector3( -532.494f, -890.133f, 24.770f - 0.9f );

		private static bool _isRecordingAroundPeople;
		private static bool _isWeazelEmployee;
		private static bool _hasRentedCompanyVan;
		private static bool _isCheckingVehicleStorage;
		private static bool _needsJobTraining;
		private static int _jobTrainingCount;

		private static int _cameraHandle;
		private static bool _hasCameraInHand;
		private static bool _cameraInitialized;
		private static bool _isFilming;
		private static bool _isOnAir;
		private static bool _isUsingZoomCamera;
		private static bool _cameraControlsInitialized;
		private static bool _displayedCameraTraining;

		private static int _micHandle;
		private static bool _micControlsInitialized;
		private static bool _hasMicInHand;
		private static bool _isSpeakingToCamera;
		private static bool _isInterview;
		private static bool _displayedMicTraining;

		private static int _currentRecordingTimeMin;
		private static int _currentRecordingTimeSec;
		private static bool _uploadingData;

		private static int _startingHealth;
		private static CitizenFX.Core.Vehicle _rentedNewsVan;
		private static CitizenFX.Core.Vehicle _lastNewsVehicle;

		private static Scaleform _scaleform;
		private static Camera _camera;

		private static Stance.StanceStates _previousStance;

		private static JobStates _state = JobStates.Unemployed;

		public static void Init() {
			try {
				_rand = new Random();
				Client.ActiveInstance.RegisterTickHandler( OnTick );

				CreateBlip();
				var weazelNewsMarker = new Marker( JobStartLocation, MarkerType.HorizontalCircleFat,
					Color.FromArgb( 75, 219, 197, 146 ), 3f * Vector3.One, new Vector3( 0, 0, 0 ), new Vector3( 0, 0, 0 ) );
				MarkerHandler.All.Add( MarkerHandler.All.Count, weazelNewsMarker );

				Client.ActiveInstance.ClientCommands.Register( "/uploadnewsreport", HandleMediaUploadCommand );
				Client.ActiveInstance.ClientCommands.Register( "/getnewsequip", RetrieveEquipmentCommand );
				Client.ActiveInstance.ClientCommands.Register( "/storenewsequip", StoreEquipmentCommand );
				Client.ActiveInstance.ClientCommands.Register( "/weazelnewshelp", DisplayHelpMessageCommand );

				RegisterMenus();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Registers interaction menu for job
		/// </summary>
		private static void RegisterMenus()
		{
			try
			{
				InteractionListMenu.RegisterInteractionMenuItem(new MenuItemStandard { Title = "Store News Equipment", OnActivate = StoreEquipmentWrapper }, () => _isWeazelEmployee && (_hasMicInHand || _hasCameraInHand), 514);
				InteractionListMenu.RegisterInteractionMenuItem(new MenuItemStandard { Title = "Get News Cam", OnActivate = RetrieveCameraWrapper}, () => _isWeazelEmployee && !_hasCameraInHand && !_hasMicInHand, 513);
				InteractionListMenu.RegisterInteractionMenuItem(new MenuItemStandard { Title = "Get News Mic", OnActivate = RetrieveMicWrapper }, () => _isWeazelEmployee && !_hasCameraInHand && !_hasMicInHand, 512);
				InteractionListMenu.RegisterInteractionMenuItem(new MenuItemStandard { Title = "Upload News Report", OnActivate = UploadNewsReportWrapper }, () => _isWeazelEmployee && !_uploadingData && _currentRecordingTimeMin > 0, 511);
				InteractionListMenu.RegisterInteractionMenuItem(new MenuItemStandard { Title = "Display News Help", OnActivate = DisplayHelpMessageWrapper }, () => _isWeazelEmployee && (_hasCameraInHand || _hasMicInHand), 510);
			}
			catch (Exception ex){
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Should only be called from the UI which validates the user is on active job
		/// </summary>
		/// <param name="item">menu item</param>
		private static async void RetrieveCameraWrapper(MenuItemStandard item)
		{
			try
			{
				if ( CurrentPlayer.Ped.Weapons.Current != WeaponHash.Unarmed )
				{
					BaseScript.TriggerEvent("Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"You need to put away what is in your hands first.");
					return;
				}

				await CheckNewsvanStorageAccess(NewsEquipment.NewsCamera, true);
			}
			catch (Exception ex){
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Should only be called from the UI which validates the user is on active job
		/// </summary>
		/// <param name="item">menu item</param>
		private static async void RetrieveMicWrapper(MenuItemStandard item)
		{
			try
			{
				if ( CurrentPlayer.Ped.Weapons.Current != WeaponHash.Unarmed )
				{
					BaseScript.TriggerEvent("Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"You need to put away what is in your hands first.");
					return;
				}

				await CheckNewsvanStorageAccess(NewsEquipment.Microphone, true);
			}
			catch (Exception ex){
				Log.Error( ex );
			}
		}
		
		/// <summary>
		///     Should only be called from the UI which validates the user is on active job
		/// </summary>
		/// <param name="item">menu item</param>
		private static async void StoreEquipmentWrapper(MenuItemStandard item)
		{
			try
			{
				var equipToStore = _hasCameraInHand ? NewsEquipment.NewsCamera : NewsEquipment.Microphone;
				await CheckNewsvanStorageAccess(equipToStore, false);
			}
			catch (Exception ex){
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Should only be called from the UI which validates the user is on active job
		/// </summary>
		/// <param name="item">menu item</param>
		private static async void UploadNewsReportWrapper(MenuItemStandard item)
		{
			try {
				await HandleMediaUpload();
			}
			catch (Exception ex){
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Should only be called from the UI which validates the user is on active job
		/// </summary>
		/// <param name="item">menu item</param>
		private static void DisplayHelpMessageWrapper(MenuItemStandard item)
		{
			try {
				DisplayHelpMessage();
			}
			catch (Exception ex){
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle media upload
		/// </summary>
		/// <param name="command"></param>
		private static void DisplayHelpMessageCommand( Command command ) {
			try {
				if( !_isWeazelEmployee || _needsJobTraining || (!_hasCameraInHand && !_hasMicInHand) ) {
					BaseScript.TriggerEvent("Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"You need equipment in your hand before you can read the manual for it.");
					return;
				}

				DisplayHelpMessage();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle media upload command
		/// </summary>
		/// <param name="command"></param>
		private static async void HandleMediaUploadCommand( Command command ) {
			try {
				if( !_isWeazelEmployee || _uploadingData ) return;

				await HandleMediaUpload();

			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Retrieve job equipment from news van storage
		/// </summary>
		/// <param name="command"></param>
		private static async void RetrieveEquipmentCommand( Command command ) {
			try {
				if( !_isWeazelEmployee ) return;

				if( _hasCameraInHand || _hasMicInHand || CurrentPlayer.Ped.Weapons.Current != WeaponHash.Unarmed ) {
					BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"You need to put away what is in your hands first." );
					return;
				}

				var args = command.Args.ToString();
				if( args.Contains( "mic" ) ) await CheckNewsvanStorageAccess( NewsEquipment.Microphone, true );
				else if( args.Contains( "cam" ) ) await CheckNewsvanStorageAccess( NewsEquipment.NewsCamera, true );
				else {
					BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"Usage: /getnewsequip [mic|cam] e.g. /getnewsequip cam | Note: You must be at the opened back of a rented news van to retrieve equipment." );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Store job equipment in news van storage
		/// </summary>
		/// <param name="command"></param>
		private static async void StoreEquipmentCommand( Command command ) {
			try {
				if( !_isWeazelEmployee ) return;

				if( !_hasCameraInHand && !_hasMicInHand ) return;

				var equipToStore = _hasCameraInHand ? NewsEquipment.NewsCamera : NewsEquipment.Microphone;
				await CheckNewsvanStorageAccess( equipToStore, false );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Create Weazel News HQ Blip
		/// </summary>
		private static void CreateBlip() {
			try {
				var weazelNewsBlip = World.CreateBlip( JobStartLocation );
				weazelNewsBlip.Scale = 0.9f;
				weazelNewsBlip.Sprite = BlipSprite.Camera;
				weazelNewsBlip.Color = BlipColor.Blue;
				weazelNewsBlip.IsShortRange = true;
				weazelNewsBlip.Name = "Weazel News";
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     WeazelNews OnTick
		/// </summary>
		private static async Task OnTick() {
			try {
				if( !Session.HasJoinedRP || !CurrentPlayer.CharacterData.Duty.HasFlag( Duty.WeazelNews ) ) return;
				_scaleform?.Render2D();

				switch( _state ) {
				case JobStates.Unemployed:
					if( IsAtWeazelNewsHq() ) {
						Screen.DisplayHelpTextThisFrame( "Press ~INPUT_PICKUP~ to take a Weazel News job" );
						if( !Game.IsControlJustPressed( 0, Control.Pickup ) ) return;

						ToggleWeazelNewsEmployment( true );
					}
					break;
				case JobStates.Employed:
					if( !_isWeazelEmployee ) {
						_state = JobStates.Unemployed;
						return;
					}

					var ped = CurrentPlayer.Ped;
					if( _hasCameraInHand ) {
						DisableControls();
						if( !_cameraControlsInitialized ) {
							//Block prone stance when holding camera
							Stance.HandleBlockingEventWrapper( false, true );
							CurrentPlayer.EnableWeaponWheel( false );
							_cameraControlsInitialized = true;
						}
						//Check if player should drop the equipment in their hand
						if( Cache.PlayerHealth > _startingHealth ) _startingHealth = Cache.PlayerHealth;
						if( CheckIfEquipDropped( ped ) ) return;
						//Check if job control button presses occured
						if( ControlHelper.IsControlJustPressed( Control.Context, modifier: ControlModifier.Alt ) ) {
							await ToggleFilmingState( ped );
						}
						else if( ControlHelper.IsControlJustPressed( Control.SelectWeaponUnarmed, modifier: ControlModifier.Alt ) ) {
							if( _isOnAir ) {
								StopLiveFilming();
							}
							else if( _isFilming ) {
								StartLiveFilming();
								LoadZoomCamera();
							}
						}

						//Make sure the player is always doing the filming animation if the camera is on their shoulder
						if( _isFilming ) {
							if( !API.IsEntityPlayingAnim( ped.Handle, "cellphone@", "f_cellphone_call_listen_base", 3 ) )
								await Game.PlayerPed.Task.PlayAnimation( "cellphone@", "f_cellphone_call_listen_base", 3f, 3f, -1,
									(AnimationFlags)49, 0 );
							//Reattach camera prop at a slightly different angle when kneeling vs standing
							var currentStance = Stance.State;
							if( currentStance != _previousStance ) {
								if( currentStance == Stance.StanceStates.Crouch || _previousStance == Stance.StanceStates.Crouch )
									AttachCamera( true );
								_previousStance = currentStance;
							}

							DisableControlsWhileFilming();

							if( _isUsingZoomCamera ) {
								UseZoomCamera();
							}
							return;
						}
					}
					else {
						if( _cameraControlsInitialized ) {
							//Unblock prone stance
							Stance.HandleBlockingEventWrapper( false, false );
							CurrentPlayer.EnableWeaponWheel( true );
							_cameraControlsInitialized = false;
						}

						if( _hasMicInHand ) {
							DisableControls();
							if( !_micControlsInitialized ) {
								CurrentPlayer.EnableWeaponWheel( false );
								_micControlsInitialized = true;
							}

							if( Cache.PlayerHealth > _startingHealth ) _startingHealth = Cache.PlayerHealth;
							if( CheckIfEquipDropped( ped ) ) return;

							if( ControlHelper.IsControlJustPressed( Control.Context, modifier: ControlModifier.Alt ) ) {
								_isInterview = false;
								_isSpeakingToCamera = !_isSpeakingToCamera;
								await ToggleMicState( ped );
							}
							else if( ControlHelper.IsControlJustPressed( Control.SelectWeaponUnarmed, modifier: ControlModifier.Alt ) ) {
								_isSpeakingToCamera = false;
								_isInterview = !_isInterview;
								await ToggleMicState( ped );
							}
						}
						else {
							if( _micControlsInitialized ) {
								CurrentPlayer.EnableWeaponWheel( true );
								_micControlsInitialized = false;
							}
						}
					}

					if( IsAtWeazelNewsHq() ) {
						if( !_needsJobTraining ) {
							Screen.DisplayHelpTextThisFrame( "Press ~INPUT_PICKUP~ to quit your Weazel News job" );
							if( !Game.IsControlJustPressed( 0, Control.Pickup ) ) return;

							ToggleWeazelNewsEmployment( false );
						}
					}

					if( !_hasRentedCompanyVan ) {
						var distance = ped.Position.DistanceToSquared( VehicleRentalLocation );
						if( distance < 500 && distance > 1.25 ) DrawVehicleRentalMarker( false );
						if( distance < 16 && distance > 1.25 ) {
							if( !_needsJobTraining ) {
								Screen.DisplayHelpTextThisFrame( "Press ~INPUT_PICKUP~ to rent a Weazel News van" );
							}
							if( !Game.IsControlJustPressed( 0, Control.Pickup ) ) return;

							var hasEnoughMoney = await CollectSafetyDeposit();
							if( hasEnoughMoney ) HandleJobVehicleRental();
						}
					}

					if( Cache.IsPlayerInVehicle ) {
						_lastNewsVehicle = null;
						var currentVehicle = Cache.CurrentVehicle;
						if( currentVehicle == null || !currentVehicle.Exists() || !Cache.IsPlayerDriving ) return;

						await CheckVehicleRentalReturn( ped );
						await BaseScript.Delay( 3000 );
					}
					else {
						if(  _lastNewsVehicle == null || !_lastNewsVehicle.Exists() ) {
							var lastVehicle = ped.LastVehicle;
							if( lastVehicle == null || !lastVehicle.Exists() || lastVehicle.Model.Hash != (int)VehicleHash.Rumpo ||
							    API.GetVehicleLivery( lastVehicle.Handle ) != 2 ) return;
							_lastNewsVehicle = lastVehicle;
						}
					}
					break;
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Calculate how much work the player is actively doing
		/// </summary>
		private static async Task EmploymentTask() {
			try {
				if( _needsJobTraining ) {
					_jobTrainingCount = _jobTrainingCount + 1;
					if( _jobTrainingCount > 60 ) {
						_jobTrainingCount = 0;
						_needsJobTraining = false;
					}
				}

				var isWorking = _isOnAir || _isInterview || _isSpeakingToCamera || Filming.IsFilming;
				if( isWorking && !_uploadingData ) {
					if( DateTime.Now.CompareTo( _onAirTickTime ) >= 0 ) {
						_onAirTickTime = DateTime.Now + TimeSpan.FromSeconds( 1 );
						if( _isRecordingAroundPeople ) {
							_currentRecordingTimeSec = _currentRecordingTimeSec + 1;
						}
						if( _currentRecordingTimeSec >= 60 ) {
							if( _currentRecordingTimeMin >= MaxRecordingTimeMin ) {
								Screen.DisplayHelpTextThisFrame(
									"Your storage is full, get back to a vehicle and upload it so you get paid for your work." );
								await BaseScript.Delay( 30000 );
							}
							else {
								_currentRecordingTimeMin = _currentRecordingTimeMin + 1;
							}
							_currentRecordingTimeSec = 0;
						}
					}
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
			await BaseScript.Delay( 250 );
		}

		/// <summary>
		///     Periodically check if the prop still exists, else create it
		/// </summary>
		private static async Task PeriodicCheck() {
			try {
				if( _hasCameraInHand ) {
					var entity = Entity.FromHandle( _cameraHandle );
					if( entity == null || !entity.Exists() ) {
						await CreateNewsEquipment( NewsEquipment.NewsCamera );
						AttachCamera( _isFilming );
					}
				}
				else if( _hasMicInHand ) {
					var entity = Entity.FromHandle( _micHandle );
					if( entity == null || !entity.Exists() ) {
						await CreateNewsEquipment( NewsEquipment.Microphone );
						AttachMicrophone( _isSpeakingToCamera );
					}
				}
				_isRecordingAroundPeople = PedInteraction.IsPlayerWithinDistanceOfPeople( 2900 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
			await BaseScript.Delay( 512 );
		}

		/// <summary>
		///     Check if equipment should be dropped
		/// </summary>
		/// <param name="ped"></param>
		private static bool CheckIfEquipDropped( Ped ped ) {
			try {
				var isDriving = false;
				if( Cache.IsPlayerInVehicle ) {
					var veh = Cache.CurrentVehicle;
					if( veh != null && veh.Exists() ) isDriving = Cache.IsPlayerDriving;
				}

				var pedHandle = Cache.PlayerHandle;
				var hasTakenDmg = Cache.PlayerHealth < _startingHealth;
				if( isDriving || hasTakenDmg || API.IsPedUsingAnyScenario( pedHandle ) ||
				    Arrest.PlayerCuffState != CuffState.None ||
				    API.IsEntityPlayingAnim( pedHandle, "mp_arresting", "idle", 3 ) ||
				    API.IsEntityPlayingAnim( pedHandle, "random@mugging3", "handsup_standing_base", 3 ) ||
				    API.IsEntityPlayingAnim( pedHandle, "random@arrests@busted", "idle_a", 3 ) ||
				    ped.IsInWater || CurrentPlayer.Ped.IsClimbing || ( CurrentPlayer.Ped.IsJumping && _hasCameraInHand ) ) {
					if( _hasMicInHand || _hasCameraInHand ) {
						var equipmentToDrop = _hasMicInHand ? NewsEquipment.Microphone : NewsEquipment.NewsCamera;
						DropEquipment( equipmentToDrop );
					}
					if( _isFilming ) StopLiveFilming();

					GoOffAir();
					return true;
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
			return false;
		}

		/// Credit: Mooshe
		/// <summary>
		///     Uze the adjustable zoom camera
		/// </summary>
		private static void UseZoomCamera() {
			try {
				if( !_isOnAir ) return;

				if( _camera == null ) {
					const float defaultFov = 16;
					_camera = World.CreateCamera( Game.PlayerPed.Bones[Bone.IK_Head].Position + new Vector3( 0f, 0f, 0.25f ),
						Game.PlayerPed.Rotation, defaultFov );
					World.RenderingCamera = _camera;
					_camera.Shake( CameraShake.Hand, 0.00025f );
					_camera.FarDepthOfField = 0;
					_camera.NearDepthOfField = 0;
					_camera.DepthOfFieldStrength = 0;
					API.AttachCamToEntity( _camera.Handle, _cameraHandle, 0.245f, 0, 0, true );
				}
				const float minFov = 5;
				if( ControlHelper.IsControlPressed( Control.VehicleNextRadioTrack ) && _camera.FieldOfView > minFov )
					_camera.FieldOfView -= 0.10f;
				const float maxFov = 24;
				if( ControlHelper.IsControlPressed( Control.VehiclePrevRadioTrack ) && _camera.FieldOfView < maxFov )
					_camera.FieldOfView += 0.10f;

				CheckInputRotation();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Stop using the adjustable zoom camera
		/// </summary>
		private static void StopZoomCamera() {
			try {
				_isUsingZoomCamera = false;
				_cameraInitialized = false;
				_camera?.Detach();
				_camera?.Delete();
				_camera = null;
				World.RenderingCamera = null;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// Credit: Mooshe
		/// <summary>
		///     Check if player is rotating view and change camera
		/// </summary>
		private static void CheckInputRotation() {
			try {
				if( _camera == null || !_isOnAir )
					return;

				var rightAxisX = Game.GetDisabledControlNormal( 0, (Control)220 );
				var rightAxisY = Game.GetDisabledControlNormal( 0, (Control)221 );

				if( !(Math.Abs( rightAxisX ) > 0) && !(Math.Abs( rightAxisY ) > 0) ) return;
				_camera.StopShaking();
				var rotation = _camera.Rotation;
				rotation.Z += rightAxisX * -8f;

				const float maxX = 45f, minX = -35f;
				var yValue = rightAxisY * -5f;
				if( rotation.X + yValue > minX && rotation.X + yValue < maxX )
					rotation.X += yValue;
				_camera.Rotation = rotation;
				_camera.Shake( CameraShake.Hand, 0.00025f );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Check if the player wants to return their rental vehicle
		/// </summary>
		/// <param name="ped"></param>
		private static async Task CheckVehicleRentalReturn( Ped ped ) {
			try {
				if( !_hasRentedCompanyVan ) return;
				var distance = ped.Position.DistanceToSquared( RentalVehicleReturn );
				if( distance < 500 && distance > 1.25 ) DrawVehicleRentalMarker( true );
				if( !(distance < 50) || !(distance > 1.25) ) return;

				Screen.DisplayHelpTextThisFrame( "Press ~INPUT_PICKUP~ to return Weazel News van" );
				if( !Game.IsControlJustPressed( 0, Control.Pickup ) ) return;

				await ReturnCompanyVehicle( ped );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Toggle Weazel News employment status
		/// </summary>
		/// <param name="isWorking">Is the player a current Weazel News employee</param>
		private static void ToggleWeazelNewsEmployment( bool isWorking ) {
			try {
				_isWeazelEmployee = isWorking;
				_state = isWorking ? JobStates.Employed : JobStates.Unemployed;

				if( isWorking ) {
					_needsJobTraining = true;
					DisplayJobTrainingMessage();
					Client.ActiveInstance.RegisterTickHandler( EmploymentTask );
					Client.ActiveInstance.RegisterTickHandler( PeriodicCheck );
				}
				else {
					Client.ActiveInstance.DeregisterTickHandler( EmploymentTask );
					Client.ActiveInstance.DeregisterTickHandler( PeriodicCheck );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Display job training message
		/// </summary>
		private static async void DisplayJobTrainingMessage() {
			try {
				while( _needsJobTraining ) {
					Screen.DisplayHelpTextThisFrame( $"-Weazel News-\n/getnewsequip [cam|mic] to retrieve equipment\n/storenewsequip to store equipment\n/uploadnewsreport to deliver footage to HQ for pay\n/weazelnewshelp for equipment keybind help" );
					await BaseScript.Delay( 0 );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Display equipment training message
		/// </summary>
		/// <param name="equip"></param>
		private static async void DisplayEquipmentTrainingMessage(NewsEquipment equip) {
			try {
				while( _needsJobTraining ) {
					Screen.DisplayHelpTextThisFrame( equip == NewsEquipment.NewsCamera
						? $"Alt+~INPUT_PICKUP~ toggle camera on shoulder\nAlt+~INPUT_SELECT_WEAPON_UNARMED~ toggle live on air\n~INPUT_VEH_NEXT_RADIO_TRACK~ and ~INPUT_VEH_PREV_RADIO_TRACK~ camera zoom in and out"
						: $"Alt+~INPUT_PICKUP~ toggle speaking into microphone\nAlt+~INPUT_SELECT_WEAPON_UNARMED~ toggle interview" );
					await BaseScript.Delay( 0 );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Pay the player for uploading their report
		/// </summary>
		/// <param name="recordingTimeMin"></param>
		private static void PayPlayerForUpload( int recordingTimeMin ) {
			try {
				//Calculate job pay
				var payAmount =
					(int)Math.Round(
						_rand.NextGaussian( recordingTimeMin * (Economy.WeazelNewsPerHour / 2), 3 ) );
				if( payAmount <= 0 ) return;

				var successTimestamp = DateTime.UtcNow;
				var eventData =
					$"PAYMENT|WEAZELNEWSJOBSUBMISSION|SUCCESS|TS1={successTimestamp.ToLocalTime()}|A={payAmount}";
				const string jobSource = "WeazelNewsJobSubmission";
				PayPlayerDirectDeposit( payAmount, eventData, jobSource );
				_currentRecordingTimeMin = 0;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Toggle the state of the micrphone position
		/// </summary>
		/// <param name="ped"></param>
		private static async Task ToggleMicState( Ped ped ) {
			try {
				ped.Task.ClearAll();
				if( _isSpeakingToCamera ) {
					_onAirTickTime = DateTime.Now + TimeSpan.FromSeconds( 1 );
					Stance.HandleBlockingEventWrapper( false, true );
					await ped.Task.PlayAnimation( "amb@world_human_drinking@coffee@male@base", "base", 3f, 3f, -1,
						(AnimationFlags)49, 0 );
				}
				else {
					if( _isInterview ) {
						_onAirTickTime = DateTime.Now + TimeSpan.FromSeconds( 1 );
						Stance.HandleBlockingEventWrapper( false, true );
						await ped.Task.PlayAnimation( "missmic4premiere", "interview_short_lazlow", 3f, 3f, -1,
							(AnimationFlags)49, 0 );
					}
					else {
						Stance.HandleBlockingEventWrapper( false, false);
					}

				}
				await BaseScript.Delay( 175 );
				AttachMicrophone( _isSpeakingToCamera );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Toglle the position of the video camera
		/// </summary>
		/// <param name="ped"></param>
		private static async Task ToggleFilmingState( Ped ped ) {
			try {
				ped.Task.ClearAll();
				if( !_isFilming ) {
					await ped.Task.PlayAnimation( "cellphone@", "f_cellphone_call_listen_base", 3f, 3f, -1,
						(AnimationFlags)49, 0 );
					await BaseScript.Delay( 300 );
					_isFilming = true;
				}
				else {
					if( _isOnAir ) StopLiveFilming();
					_isFilming = false;
				}
				_previousStance = Stance.State;
				AttachCamera( _isFilming );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Start live filming with video camera and apply Weazel News overlay
		/// </summary>
		private static async void StartLiveFilming() {
			try {
				if( _cameraInitialized ) return;

				API.DoScreenFadeOut( 500 );

				var timer = Game.GameTime;
				while( !API.IsScreenFadedOut() ) {
					if( Game.GameTime - timer > 2000 )
						break;
					await BaseScript.Delay( 50 );
				}

				_scaleform = new Scaleform( "breaking_news" );
				while( !_scaleform.IsLoaded ) await BaseScript.Delay( 0 );
				CinematicMode.DoHideHud = true;

				API.DoScreenFadeIn( 500 );

				_isOnAir = true;
				_onAirTickTime = DateTime.Now + TimeSpan.FromSeconds( 1 );
				_cameraInitialized = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Stop live filming with video camera and remove Weazel News overlay
		/// </summary>
		private static async void StopLiveFilming() {
			try {
				if( !_isOnAir || !_isFilming ) return;
				API.DoScreenFadeOut( 500 );

				var timer = Game.GameTime;
				while( !API.IsScreenFadedOut() ) {
					if( Game.GameTime - timer > 2000 )
						break;
					await BaseScript.Delay( 10 );
				}
				CinematicMode.DoHideHud = false;
				_scaleform?.Dispose();
				_scaleform = null;
				_isOnAir = false;

				StopZoomCamera();

				API.DoScreenFadeIn( 500 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Load the adjustable zoom camera
		/// </summary>
		private static void LoadZoomCamera() {
			try {
				_isUsingZoomCamera = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Return whether or not the news van trunk is open
		/// </summary>
		/// <param name="vehicle"></param>
		private static bool IsTrunkOpen( CitizenFX.Core.Vehicle vehicle ) {
			try {
				return vehicle.Doors[VehicleDoorIndex.BackLeftDoor].IsOpen ||
				       vehicle.Doors[VehicleDoorIndex.BackRightDoor].IsOpen || 
				       vehicle.Doors[VehicleDoorIndex.BackLeftDoor].IsBroken || 
				       vehicle.Doors[VehicleDoorIndex.BackRightDoor].IsBroken;
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return false;
			}
		}

		/// <summary>
		///     Check if player can access newsvan to retrieve/store an item and if it has inventory
		/// </summary>
		/// <param name="equipment"></param>
		/// <param name="isRetrieving">is retrieving equipment else is storing</param>
		private static async Task CheckNewsvanStorageAccess( NewsEquipment equipment, bool isRetrieving ) {
			try {
				//Acccess news van storage
				if( _lastNewsVehicle == null || !_lastNewsVehicle.Exists() ) return;

				if( _isCheckingVehicleStorage ) return;

				_isCheckingVehicleStorage = true;

				var pedPos = Cache.PlayerPos;
				var backLeftDoorDistance = _lastNewsVehicle.Bones["door_dside_r"].Position.DistanceToSquared( pedPos );
				var backRightDoorDistance = _lastNewsVehicle.Bones["door_pside_r"].Position.DistanceToSquared( pedPos );

				if( (backLeftDoorDistance < 1.7 && backRightDoorDistance < 3 ||
				     backRightDoorDistance < 1.7 && backLeftDoorDistance < 3) && IsTrunkOpen( _lastNewsVehicle ) ) {
					CurrentPlayer.Ped.Task.ClearAll();
					//Bend over to access trunk
					CurrentPlayer.Ped.Task.PlayAnimation( "pickup_object", "pickup_low", -8f, 500, AnimationFlags.None );
					await BaseScript.Delay( 450 );

					if( isRetrieving ) {
						_startingHealth = Cache.PlayerHealth;

						GrabEquipment( equipment );

						//Only display training message once
						if( (equipment == NewsEquipment.Microphone && !_displayedMicTraining) || (equipment == NewsEquipment.NewsCamera && !_displayedCameraTraining) ) {
							_needsJobTraining = true;
							DisplayEquipmentTrainingMessage( equipment );
							if( equipment == NewsEquipment.Microphone ) {
								_displayedMicTraining = true;
							}
							else {
								_displayedCameraTraining = true;
							}
						}
						await BaseScript.Delay( 1000 );
					}
					else {
						StoreEquipment( equipment );
					}
				}
				else {
					BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"You need to open the doors and access the storage from the rear." );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
			_isCheckingVehicleStorage = false;
		}

		/// <summary>
		///     Store job equipment in vehicle
		/// </summary>
		/// <param name="equipment"></param>
		private static void StoreEquipment( NewsEquipment equipment ) {
			try {
				DeleteEquipment( equipment );
				GoOffAir();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Create news equipment and attach it to the player
		/// </summary>
		/// <param name="equipment"></param>
		private static async void GrabEquipment( NewsEquipment equipment ) {
			try {
				var equipmentHandle = await CreateNewsEquipment( equipment );

				var entity = Entity.FromHandle( equipmentHandle );
				if( entity == null ) {
					Log.Error( "Error: Non-valid handle grabbing equipment from Newsvan." );
					return;
				}
				if( equipment == NewsEquipment.NewsCamera ) {
					_cameraHandle = equipmentHandle;
					AttachCamera( false );
					_hasCameraInHand = true;
				}
				else {
					_micHandle = equipmentHandle;
					AttachMicrophone( false );
					_hasMicInHand = true;
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Create the news equipment
		/// </summary>
		/// <param name="equipment"></param>
		private static async Task<int> CreateNewsEquipment( NewsEquipment equipment ) {
			try {
				var model = equipment == NewsEquipment.NewsCamera
					? new Model( "prop_v_cam_01" )
					: new Model( "p_ing_microphonel_01" );
				await model.Request( 250 );

				if( !model.IsInCdImage || !model.IsValid ) return -1;

				while( !model.IsLoaded ) await BaseScript.Delay( 10 );

				var offsetPosition = CurrentPlayer.Ped.GetOffsetPosition( Vector3.One );
				var attachPosition = API.GetPedBoneCoords( Cache.PlayerHandle, (int)Bone.SKEL_R_Hand, offsetPosition.X,
					offsetPosition.Y,
					offsetPosition.Z );

				var prop = await World.CreateProp( model, attachPosition, new Vector3( 0, 0, 0 ),
					false, false );
				model.MarkAsNoLongerNeeded();
				return prop.Handle;
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return -1;
			}
		}

		/// <summary>
		///     Drop the news equipment
		/// </summary>
		/// <param name="equipment"></param>
		private static void DropEquipment( NewsEquipment equipment ) {
			try {
				_hasCameraInHand = false;
				_hasMicInHand = false;

				var handle = equipment == NewsEquipment.NewsCamera ? _cameraHandle : _micHandle;
				var entity = Entity.FromHandle( handle );
				if( entity == null || !entity.Exists() ) return;

				API.DetachEntity( handle, true, true );
				entity.MarkAsNoLongerNeeded();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Delete the news equipment
		/// </summary>
		/// <param name="equipment"></param>
		public static void DeleteEquipment( NewsEquipment equipment ) {
			try {
				var isCamera = equipment == NewsEquipment.NewsCamera;
				var equipHandle = isCamera ? _cameraHandle : _micHandle;

				if( equipHandle <= 0 ) return;

				if( isCamera ) {
					_hasCameraInHand = false;
					_cameraHandle = 0;
				}
				else {
					_hasMicInHand = false;
					_micHandle = 0;
				}

				var entity = Entity.FromHandle( equipHandle );
				if( entity == null || !entity.Exists() ) {
					Log.Error( $"WeazelNews: equipHandle={equipHandle}" );
					return;
				}
				entity.Delete();
				entity.MarkAsNoLongerNeeded();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Attach microphone to players hand
		/// </summary>
		/// <param name="isSpeakingToCamera"></param>
		private static void AttachMicrophone( bool isSpeakingToCamera ) {
			try {
				const int handBone = (int)Bone.SKEL_R_Hand;
				var xPos = 0.1f;
				var zPos = 0f;
				var xRot = -45f;
				var zRot = 0f;

				if( isSpeakingToCamera ) {
					xPos = 0.11f;
					zPos = -0.02f;
					xRot = -90f;
					zRot = -15f;
				}
				API.AttachEntityToEntity( _micHandle, Cache.PlayerHandle,
					API.GetPedBoneIndex( Cache.PlayerHandle, handBone ), xPos,
					0.05f, zPos, xRot,
					0f, zRot,
					true,
					true, false, true, 1, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Attach camera to players hand or shoulder
		/// </summary>
		/// <param name="isFilming"></param>
		private static void AttachCamera( bool isFilming ) {
			try {
				const int handBone = (int)Bone.SKEL_R_Hand;
				var boneId = handBone;
				var xPos = 0.25f;
				var yPos = 0.025f;
				var zPos = 0f;
				var xRot = 0f;
				var yRot = -90f;
				var zRot = 45f;

				if( isFilming ) {
					const int shoulderBone = (int)Bone.SKEL_R_Clavicle;
					boneId = shoulderBone;
					if( Stance.State == Stance.StanceStates.Crouch ) {
						xPos = 0.02f;
						yPos = -0.015f;
						zPos = 0.165f;
						xRot = -60f;
						yRot = 50f;
						zRot = 150f;
					}
					else {
						xPos = 0.04f;
						yPos = 0.05f;
						zPos = 0.13f;
						xRot = -45f;
						yRot = 25f;
						zRot = 120f;
					}
				}
				API.AttachEntityToEntity( _cameraHandle, Cache.PlayerHandle,
					API.GetPedBoneIndex( Cache.PlayerHandle, boneId ), xPos,
					yPos, zPos, xRot,
					yRot, zRot,
					true,
					true, false, true, 1, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle job vehicle rental return
		/// </summary>
		private static async void HandleJobVehicleRental() {
			try {
				var parkingSpot = VehicleInteraction.FindParkingSpot(WeazelNewsTruckSpawnSpaces);
				if( parkingSpot < 0 ) {
					BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"No parking spots available, come back another time." );
					return;
				}

				_hasRentedCompanyVan = true;
				_rentedNewsVan = await World.CreateVehicle( VehicleHash.Rumpo,
					new Vector3( WeazelNewsTruckSpawnSpaces[parkingSpot].X, WeazelNewsTruckSpawnSpaces[parkingSpot].Y, WeazelNewsTruckSpawnSpaces[parkingSpot].Z ), WeazelNewsTruckSpawnSpaces[parkingSpot].W );
				Function.Call( Hash.SET_VEHICLE_LIVERY, _rentedNewsVan, 2 );
				await BaseScript.Delay( 250 );

				var handle = _rentedNewsVan.Handle;
				_rentedNewsVan.Mods.LicensePlate = _rentedNewsVan.Mods.LicensePlate.Replace( _rentedNewsVan.Mods.LicensePlate[0], 'W' );
				Function.Call<bool>( Hash.DECOR_SET_BOOL, handle, "Vehicle.PlayerOwned", false );
				Function.Call<bool>( Hash.DECOR_SET_BOOL, handle, "Vehicle.PlayerTouched", true );
				Function.Call( Hash.DECOR_SET_BOOL, handle, "Vehicle.Locked", false );
				Function.Call( Hash.DECOR_SET_INT, handle, "Vehicle.OwnerID",
					CurrentPlayer.Character.Data.CharID );
				Function.Call( Hash.DECOR_SET_INT, handle, "Vehicle.VehicleType",
					(int)VehicleRegistrationType.BusinessRental );
				Function.Call( Hash._DECOR_SET_FLOAT, handle, "Vehicle.Fuel", 100f );
				_rentedNewsVan.NeedsToBeHotwired = false;

				Classes.Vehicle.Vehicles.RecordJobVehicle( _rentedNewsVan.Mods.LicensePlate );

				API.SetEntityAsMissionEntity( handle, true, true );
				_rentedNewsVan.IsPersistent = true;
				_rentedNewsVan.IsEngineRunning = false;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Draw job vehicle rental return marker
		/// </summary>
		/// <param name="isReturn"></param>
		private static void DrawVehicleRentalMarker( bool isReturn ) {
			try {
				var location = isReturn ? RentalVehicleReturn : VehicleRentalLocation;
				var color = isReturn ? StandardColours.Vehicle : StandardColours.JobMission;

				World.DrawMarker( MarkerType.HorizontalCircleFat, location,
					new Vector3( 0, 0, 0 ),
					new Vector3( 0, 0, 0 ), 3f * Vector3.One, color, false,
					true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Return whether or not player is at Weazel News HQ
		/// </summary>
		private static bool IsAtWeazelNewsHq() {
			try {
				return DistanceToPointHelper.IsPlayerCloseToPoint( JobStartLocation, 16 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return false;
			}
		}

		/// <summary>
		///     Return job rental vehicle
		/// </summary>
		/// <param name="ped"></param>
		private static async Task ReturnCompanyVehicle( Ped ped ) {
			try {
				var entity = Entity.FromHandle( _rentedNewsVan.Handle );
				if( entity == null || !entity.Exists() ) return;

				if( Cache.IsPlayerInVehicle && Cache.CurrentVehicle == entity ) ped.Task.LeaveVehicle();
				else return;

				while( true ) {
					var vehiclePassengers = _rentedNewsVan.Passengers;
					if( vehiclePassengers.Length <= 0 ) break;
					foreach( var passenger in vehiclePassengers ) passenger.Task.LeaveVehicle();
				}
				await BaseScript.Delay( 1000 );
				await ReturnSafetyDeposit(entity);
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Collect safety deposit to rent vehicle
		/// </summary>
		private static async Task<bool> CollectSafetyDeposit() {
			try {
				var currentMoney = CurrentPlayer.Character.Data.DefaultPaymentMethod == MoneyType.Cash
					? CurrentPlayer.Character.Data.Cash
					: CurrentPlayer.Character.Data.Debit;

				if( currentMoney < JobEquipmentSecurityDepositFee ) {
					BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"Renting this job vehicle requires a {JobEquipmentSecurityDepositFee} security deposit." );
					await BaseScript.Delay( 5000 );
					return false;
				}

				BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
					$"Return the vehicle with all of the equipment to receive your deposit back." );
				var removeMoney = new RemoveMoneyModel {
					TargetCharID = CurrentPlayer.Character.Data.CharID,
					RemoveType = CurrentPlayer.Character.Data.DefaultPaymentMethod,
					Amount = JobEquipmentSecurityDepositFee
				};
				var serialize = JsonConvert.SerializeObject( removeMoney );
				BaseScript.TriggerServerEvent( "Bank.RemoveMoney", serialize );

				Log.Verbose(
					$"WeazelNews removed ${JobEquipmentSecurityDepositFee} {CurrentPlayer.Character.Data.DefaultPaymentMethod} from {CurrentPlayer.Character.Data.CharID} " );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
			return true;
		}

		/// <summary>
		///     Return the safety deposit back to the player
		/// </summary>
		private static async Task ReturnSafetyDeposit(Entity entity) {
			try {
				var vanHasCamera = false;
				var vanMicrophones = 0;
				var safetyDeposit = JobEquipmentSecurityDepositFee;

				var equipmentState = safetyDeposit < JobEquipmentSecurityDepositFee ? "missing" : "all";
				BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
					$"You returned the vehicle with {equipmentState} equipment." );

				if( safetyDeposit <= 0 ) {
					BaseScript.TriggerEvent( "Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						"Return it with at least some of the equipment next time if you want your safety deposit back." );
				}
				else {
					var successTimestamp = DateTime.UtcNow;
					var eventData =
						$"PAYMENT|WEAZELNEWSDEPOSITRETURN|SUCCESS|TS1={successTimestamp.ToLocalTime()}|A={safetyDeposit}";
					const string jobSource = "WeazelNewsDepositReturn";

					PayPlayerDirectDeposit( safetyDeposit, eventData, jobSource );

					await VehicleInteraction.RemoveVehicleFromWorld( _rentedNewsVan );
			
					_hasRentedCompanyVan = false;
					await BaseScript.Delay( 100 );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle media upload
		/// </summary>
		private static async Task HandleMediaUpload()
		{
			try
			{
				var veh = Cache.CurrentVehicle;
				if (veh == null || veh.Model.Hash != (int)VehicleHash.Rumpo || API.GetVehicleLivery(veh.Handle) != 2)
				{
					BaseScript.TriggerEvent("Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"You need to be in a work vehicle to upload back to HQ.");
					return;
				}
				//Make sure player has material to upload
				if (_currentRecordingTimeMin > 0)
				{
					_uploadingData = true;
					BaseScript.TriggerEvent("Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"Your data is being uploaded, you'll receive your paycheck shortly.");
					//Upload data delay
					var uploadTime = Rand.GetRange(_currentRecordingTimeMin, _currentRecordingTimeMin + 10) * 1000 +
					                 Rand.GetRange(0, 500);
					await BaseScript.Delay(uploadTime);
					//Uploading complete
					PayPlayerForUpload(_currentRecordingTimeMin);
					_uploadingData = false;
				}
				else
				{
					BaseScript.TriggerEvent("Chat.Message", "[WeazelNews]", StandardColours.InfoHEX,
						$"Your media storage is empty.  Get to work before you bother sending anything back to HQ.");
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		/// <summary>
		///     Pay job salary via direct deposit
		/// </summary>
		/// <param name="amount"></param>
		/// <param name="eventData"></param>
		/// <param name="jobSource"></param>
		private static void PayPlayerDirectDeposit( int amount, string eventData, string jobSource ) {
			try {
				var events = new PayMoneyEventListModel {
					Events = new List<PayMoneyEventModel> {
						new PayMoneyEventModel {
							EventTimestamp = DateTime.UtcNow,
							EventData = eventData
						}
					}.ToArray()
				};

				var paycheck = new PayMoneyModel {
					Amount = amount,
					JobSource = jobSource,
					EventData = events,
					PaymentType = MoneyType.Debit
				};
				var data = JsonConvert.SerializeObject( paycheck );
				Bank.PayMoney( data );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Display help message
		/// </summary>
		private static void DisplayHelpMessage()
		{
			try
			{
				var equipment = _hasCameraInHand ? NewsEquipment.NewsCamera : NewsEquipment.Microphone;
				_needsJobTraining = true;
				DisplayEquipmentTrainingMessage(equipment);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		/// <summary>
		///     Disable weapon switching controls when mic or camera is in hand
		/// </summary>
		private static void DisableControls() {
			try {
				API.DisableControlAction( 2, (int)Control.SelectWeapon, true );
				API.DisableControlAction( 2, (int)Control.SelectWeaponUnarmed, true );
				API.DisableControlAction( 2, (int)Control.SelectWeaponMelee, true );
				API.DisableControlAction( 2, (int)Control.SelectWeaponHandgun, true );
				API.DisableControlAction( 2, (int)Control.SelectWeaponShotgun, true );
				API.DisableControlAction( 2, (int)Control.SelectWeaponSmg, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Disable all attack controls while player is filming
		/// </summary>
		private static void DisableControlsWhileFilming() {
			try {
				API.DisableControlAction( 2, (int)Control.Attack, true );
				API.DisableControlAction( 2, (int)Control.Attack2, true );
				API.DisableControlAction( 2, (int)Control.VehicleDriveLook, true );
				API.DisableControlAction( 2, (int)Control.MeleeAttack1, true );
				API.DisableControlAction( 2, (int)Control.MeleeAttack2, true );
				API.DisableControlAction( 2, (int)Control.MeleeAttackAlternate, true );
				API.DisableControlAction( 2, (int)Control.MeleeAttackHeavy, true );
				API.DisableControlAction( 2, (int)Control.MeleeAttackLight, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Stop recording media and end all aniamtions
		/// </summary>
		private static void GoOffAir() {
			try {
				if( _isOnAir ) StopLiveFilming();

				_isFilming = false;
				_isOnAir = false;
				_hasCameraInHand = false;

				_isSpeakingToCamera = false;
				_isInterview = false;
				_hasMicInHand = false;

				CurrentPlayer.Ped.Task.ClearAnimation( "cellphone@", "f_cellphone_call_listen_base" );
				CurrentPlayer.Ped.Task.ClearAnimation( "missmic4premiere@", "interview_short_lazlow" );
				CurrentPlayer.Ped.Task.ClearAnimation( "amb@world_human_drinking@coffee@male@base@", "base" );

				Stance.HandleBlockingEventWrapper( false, false );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		private enum JobStates
		{
			Unemployed,
			Employed
		}
	}
}