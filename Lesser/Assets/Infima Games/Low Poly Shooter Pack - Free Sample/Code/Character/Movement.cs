// Copyright 2021, Infima Games. All Rights Reserved.

using System;
using System.Linq;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class Movement : MovementBehaviour
    {
        #region FIELDS SERIALIZED

        [Header("Audio Clips")]
        
        [Tooltip("The audio clip that is played while walking.")]
        [SerializeField]
        private AudioClip audioClipWalking;

        [Tooltip("The audio clip that is played while running.")]
        [SerializeField]
        private AudioClip audioClipRunning;

        [Header("Speeds")]

        [SerializeField]
        private float speedWalking = 5.0f;

        [Tooltip("How fast the player moves while running."), SerializeField]
        private float speedRunning = 9.0f;

        [Tooltip("How high the player can jump."), SerializeField]
        private float jumpForce = 300.0f;

        [Tooltip("How strong gravity is for player."), SerializeField]
        private float gravityForce = 9.81f;

        [Tooltip("How strong dash is."), SerializeField]
        private float dashForce = 10.0f;

        [Tooltip("How far dash is."), SerializeField]
        private float dashDistance = 10.0f;

        #endregion

        #region PROPERTIES

        //Velocity.
        private Vector3 Velocity
        {
            //Getter.
            get => rigidBody.velocity;
            //Setter.
            set => rigidBody.velocity = value;
        }

        #endregion

        #region FIELDS

        /// <summary>
        /// Attached Rigidbody.
        /// </summary>
        private Rigidbody rigidBody;
        /// <summary>
        /// Attached CapsuleCollider.
        /// </summary>
        private CapsuleCollider capsule;
        /// <summary>
        /// Attached AudioSource.
        /// </summary>
        private AudioSource audioSource;

        /// <summary>
        /// True if the character is currently grounded.
        /// </summary>
        private bool grounded;

        /// <summary>
        /// Player Character.
        /// </summary>
        private CharacterBehaviour playerCharacter;
        /// <summary>
        /// The player character's equipped weapon.
        /// </summary>
        private WeaponBehaviour equippedWeapon;
        
        /// <summary>
        /// Array of RaycastHits used for ground checking.
        /// </summary>
        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        /// <summary>
        /// Vertical speed for Player.
        /// </summary>
        private float verticalSpeed;

        /// <summary>
        /// Time before grounded can be off. 
        /// </summary>
        private DateTime timeBeforeAnotherGroundedState;

        /// <summary>
        /// Additional speed from dash.
        /// </summary>
        private float dashSpeed;

        /// <summary>
        /// Time end dash. 
        /// </summary>
        private DateTime timeEndDash;

        #endregion

        #region UNITY FUNCTIONS

        /// <summary>
        /// Awake.
        /// </summary>
        protected override void Awake()
        {
            //Get Player Character.
            playerCharacter = ServiceLocator.Current.Get<IGameModeService>().GetPlayerCharacter();
        }

        /// Initializes the FpsController on start.
        protected override void Start()
        {
            //Rigidbody Setup.
            rigidBody = GetComponent<Rigidbody>();
            rigidBody.constraints = RigidbodyConstraints.FreezeRotation;
            //Cache the CapsuleCollider.
            capsule = GetComponent<CapsuleCollider>();

            //Audio Source Setup.
            audioSource = GetComponent<AudioSource>();
            audioSource.clip = audioClipWalking;
            audioSource.loop = true;

            //Jump control fields
            verticalSpeed = 0;
            timeBeforeAnotherGroundedState = DateTime.MinValue;

            //Dash control fields
            dashSpeed = 0;
            timeEndDash = DateTime.MinValue;
        }

        /// Checks if the character is on the ground.
        private void OnCollisionStay()
        {
            //Bounds.
            Bounds bounds = capsule.bounds;
            //Extents.
            Vector3 extents = bounds.extents;
            //Radius.
            float radius = extents.x - 0.01f;
            
            //Cast. This checks whether there is indeed ground, or not.
            Physics.SphereCastNonAlloc(bounds.center, radius, Vector3.down,
                groundHits, extents.y - radius * 0.5f, ~0, QueryTriggerInteraction.Ignore);
            
            //We can ignore the rest if we don't have any proper hits.
            if (!groundHits.Any(hit => hit.collider != null && hit.collider != capsule)) 
                return;
            
            //Store RaycastHits.
            for (var i = 0; i < groundHits.Length; i++)
                groundHits[i] = new RaycastHit();

            //Set grounded. Now we know for sure that we're grounded.
            //grounded = true;
            grounded = timeBeforeAnotherGroundedState < DateTime.Now;
        }
			
        protected override void FixedUpdate()
        {
            //Move.
            MoveCharacter();

            //Jump.
            JumpControlCharacter();
            
            //Unground.
            grounded = false;
        }

        /// Moves the camera to the character, processes jumping and plays sounds every frame.
        protected override void Update()
        {
            //Get the equipped weapon!
            equippedWeapon = playerCharacter.GetInventory().GetEquipped();
            
            //Play Sounds!
            PlayFootstepSounds();
        }

        #endregion

        #region METHODS

        private void MoveCharacter()
        {
            #region Calculate Movement Velocity
            //Get Dash Input
            if (playerCharacter.GetIsDashButtonTapped())
                timeEndDash = DateTime.Now.AddSeconds(dashDistance / dashForce);

            dashSpeed = DateTime.Now < timeEndDash ? dashForce : 0.0f; 
            //Get Movement Input!
            Vector2 frameInput = playerCharacter.GetInputMovement();
            //Calculate local-space direction by using the player's input.
            var movement = new Vector3(frameInput.x, 0.0f, frameInput.y);

            //Running speed calculation.
            if (playerCharacter.IsRunning())
                movement *= speedRunning + dashSpeed;
            else
            {
                //Multiply by the normal walking speed.
                movement *= speedWalking + dashSpeed;
            }

            //World space velocity calculation. This allows us to add it to the rigidbody's velocity properly.
            movement = transform.TransformDirection(movement);

            #endregion
            
            //Update Velocity.
            Velocity = new Vector3(movement.x, 0.0f, movement.z);

            playerCharacter.SetTapDashButton(false);
        }

        /// <summary>
        /// Управление прыжками и гравитацией.
        /// </summary>
        /// <remarks>Вызывается в fixed update => 50 раз в секунду.</remarks>
        public void JumpControlCharacter()
        {
            #region Calculate Jump Velocity

            //Get Jump Input!
            var jumpImput = playerCharacter.GetIsJumpButtonTapped() ? 1.0f : 0.0f;
            var isNotGrounded = grounded ? 0.0f : 1.0f;
            //Если на земле, то остаётся без изменений, если в воздухе то умножается на 0
            jumpImput *= grounded ? 1.0f : 0.0f;
            //Calculate local-space direction by using the player's input.

            verticalSpeed *= isNotGrounded;
            verticalSpeed += (jumpImput * jumpForce - gravityForce * isNotGrounded) / 50;
            var movement = new Vector3(0, verticalSpeed, 0);

            //World space velocity calculation. This allows us to add it to the rigidbody's velocity properly.
            movement = transform.TransformDirection(movement);

            #endregion

            //Update Velocity.
            Velocity = Velocity + new Vector3(0.0f, movement.y, 0.0f);

            if (playerCharacter.GetIsJumpButtonTapped())
                timeBeforeAnotherGroundedState = DateTime.Now.AddMilliseconds(100);

            playerCharacter.SetTapJumpButton(false);
        } 

        /// <summary>
        /// Plays Footstep Sounds. This code is slightly old, so may not be great, but it functions alright-y!
        /// </summary>
        private void PlayFootstepSounds()
        {
            //Check if we're moving on the ground. We don't need footsteps in the air.
            if (grounded && rigidBody.velocity.sqrMagnitude > 0.1f)
            {
                //Select the correct audio clip to play.
                audioSource.clip = playerCharacter.IsRunning() ? audioClipRunning : audioClipWalking;
                //Play it!
                if (!audioSource.isPlaying)
                    audioSource.Play();
            }
            //Pause it if we're doing something like flying, or not moving!
            else if (audioSource.isPlaying)
                audioSource.Pause();
        }

        #endregion
    }
}