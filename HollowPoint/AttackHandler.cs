﻿using System;
using System.Reflection;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using MonoMod.Utils;
using MonoMod;
using static Modding.Logger;
using Modding;
using ModCommon;
using ModCommon.Util;
using Object = UnityEngine.Object;
using static HollowPoint.HollowPointEnums;

namespace HollowPoint
{
    class AttackHandler : MonoBehaviour
    { 
        public static Rigidbody2D knight;
        public static GameObject damageNumberTestGO;

        public static bool isFiring = false;
        public static bool slowWalk = false;

        static float slowWalkDisableTimer = 0;
        float clickTimer = 0;

        public HeroControllerStates h_state;
        public HeroController hc_instance;

        public void Awake()
        {
            On.NailSlash.StartSlash += OnSlash;
            On.GameManager.OnDisable += GameManager_OnDisable;
            StartCoroutine(AttackHandlerInit());        
        }

        private void GameManager_OnDisable(On.GameManager.orig_OnDisable orig, GameManager self)
        {
            GameObject go = self.gameObject;

            Destroy(go.GetComponent<HollowPointPrefabs>());
            Destroy(go.GetComponent<OrientationHandler>());
            Destroy(go.GetComponent<WeaponSwapAndStatHandler>());
            Destroy(go.GetComponent<UIHandler>());
            Destroy(go.GetComponent<DamageOverride>());
            Destroy(go.GetComponent<HollowPointSprites>());
            Destroy(go.GetComponent<HeatHandler>());
            Destroy(go.GetComponent<SpellControlOverride>());
            Destroy(go.GetComponent<Stats>());
            Destroy(go.GetComponent<AttackHandler>());

            orig(self);
        }

        public IEnumerator AttackHandlerInit()
        {
            while (HeroController.instance == null)
            {
                yield return null;
            }

            hc_instance = HeroController.instance;
            h_state = HeroController.instance.cState;
            knight = HeroController.instance.GetAttr<Rigidbody2D>("rb2d");
            damageNumberTestGO = new GameObject("damageNumberTESTCLONE", typeof(Text), typeof(CanvasRenderer), typeof(RectTransform));
            DontDestroyOnLoad(damageNumberTestGO);
        }

        public void Update()
        {
            //Melee attack with the gun out 
            if (WeaponSwapAndStatHandler.instance.currentWeapon == WeaponType.Ranged && !isFiring && hc_instance.CanCast())
            {
                if (InputHandler.Instance.inputActions.dreamNail.WasPressed)
                {
                    Log("[AttackHandler] Changing Firemode from : " + Stats.instance.cardinalFiringMode + " to : " + !Stats.instance.cardinalFiringMode );
                    Stats.instance.ToggleFireMode();
                }
                else if(OrientationHandler.heldAttack && Stats.instance.canFire)
                {

                    if(PlayerData.instance.MPCharge >= Stats.instance.SoulCostPerShot())
                    {
                        FireGun(FireModes.Single);
                    }
                    else if(clickTimer <= 0)
                    {
                        AudioHandler.PlaySoundsMisc("cantfire");
                        clickTimer = 1f;
                    }

                    //FireGun(FireModes.Burst);
                }
            }
            else if (hc_instance.cState.superDashing && !isFiring && WeaponSwapAndStatHandler.instance.currentWeapon == WeaponType.Ranged)
            {
                if (Stats.instance.canFire && OrientationHandler.heldAttack)
                {
                    Stats.instance.StartBothCooldown(4f);
                    StartCoroutine(FireGAU());
                    return;
                }

            }
            else if (!isFiring)
            {
                hc_instance.WALK_SPEED = 2.5f;
            }

            //TODO: Slow down the player while firing MOVE TO HP STATS LATER
            if (slowWalk)
            {
                h_state.inWalkZone = true;
            }
            else
            {
                h_state.inWalkZone = false;
            }

        }

        void FixedUpdate()
        {
            if (slowWalkDisableTimer > 0 && slowWalk)
            {
                slowWalkDisableTimer -= Time.deltaTime * 30f;
                if (slowWalkDisableTimer < 0)
                {
                    slowWalk = false;
                }
            }

            if (clickTimer > 0)
            {
                clickTimer -= Time.deltaTime * 1;
            }
        }

        public void OnSlash(On.NailSlash.orig_StartSlash orig, NailSlash self)
        {
            if (WeaponSwapAndStatHandler.instance.currentWeapon == WeaponType.Melee)
            {
                orig(self);
                return;
            }
        }

        public void FireGun(FireModes fm)
        {
            //if (isFiring) return;
            isFiring = true;
            Stats.instance.StartBothCooldown();

            if (Stats.instance.currentWeapon.gunName == WeaponModifierName.CARBINE)
            {
                StartCoroutine(BurstShot(3));
            }
            else if (Stats.instance.currentWeapon.gunName == WeaponModifierName.SHOTGUN)
            {
                StartCoroutine(SpreadShot(5));
            }
            else if (fm == FireModes.Single)
            {
                StartCoroutine(SingleShot());
            }

            //Gun Boosting Call Method
            float fireDegree = OrientationHandler.finalDegreeDirection;
            if (hc_instance.cState.wallSliding) StartCoroutine(KnockbackRecoil(2.5f, 270));
            else if (fireDegree == 270) StartCoroutine(KnockbackRecoil(Stats.instance.current_boostMultiplier, 270));
            else if (fireDegree < 350 && fireDegree > 190) StartCoroutine(KnockbackRecoil(0.07f, 270));
        }

        public IEnumerator SingleShot()
        {
            hc_instance.TakeMPQuick(Stats.instance.SoulCostPerShot());
            GameCameras.instance.cameraShakeFSM.SendEvent("EnemyKillShake");           
            HeatHandler.IncreaseHeat(Stats.instance.current_heatPerShot);      

            float direction = OrientationHandler.finalDegreeDirection;
            DirectionalOrientation orientation = OrientationHandler.directionOrientation;
            GameObject bullet = HollowPointPrefabs.SpawnBulletFromKnight(direction, orientation);

            BulletBehaviour hpbb = bullet.GetComponent<BulletBehaviour>();
            hpbb.bulletDamage = Stats.instance.current_damagePerShot;
            hpbb.bulletDamageScale = Stats.instance.current_damagePerLevel;
            hpbb.weaponUsed = Stats.instance.currentWeapon.gunName;
            hpbb.noDeviation = (PlayerData.instance.equippedCharm_14 && HeroController.instance.cState.onGround) ? true : false;
            hpbb.pierce = PlayerData.instance.equippedCharm_13;         
            hpbb.bulletOriginPosition = bullet.transform.position;
            hpbb.bulletSpeed = Stats.instance.current_bulletVelocity;
            hpbb.bulletDegreeDirection = direction;
            hpbb.size = Stats.instance.currentWeapon.bulletSize;

            AudioHandler.PlayGunSounds(Stats.instance.currentWeapon.gunName.ToString());
            HollowPointSprites.StartGunAnims();
            HollowPointSprites.StartFlash();
            HollowPointSprites.StartMuzzleFlash(OrientationHandler.finalDegreeDirection, 1);

            Destroy(bullet, Stats.instance.current_bulletLifetime);

            yield return new WaitForSeconds(0.02f);
            isFiring = false;
        }

        public IEnumerator BurstShot(int burst)
        {
            hc_instance.TakeMP(Stats.instance.SoulCostPerShot());
            HeatHandler.IncreaseHeat(33);
            GameCameras.instance.cameraShakeFSM.SendEvent("EnemyKillShake");
            float direction = OrientationHandler.finalDegreeDirection;
            DirectionalOrientation orientation = OrientationHandler.directionOrientation;

            for (int i = 0; i < burst; i++)
            {
                GameObject bullet = HollowPointPrefabs.SpawnBulletFromKnight(direction, orientation);
                BulletBehaviour hpbb = bullet.GetComponent<BulletBehaviour>();
                hpbb.bulletDamage = Stats.instance.current_damagePerShot;
                hpbb.bulletDamageScale = Stats.instance.current_damagePerLevel;
                hpbb.weaponUsed = Stats.instance.currentWeapon.gunName;
                hpbb.noDeviation = (PlayerData.instance.equippedCharm_14 && HeroController.instance.cState.onGround) ? true : false;
                hpbb.pierce = PlayerData.instance.equippedCharm_13;
                hpbb.bulletOriginPosition = bullet.transform.position;
                hpbb.bulletSpeed = Stats.instance.current_bulletVelocity;
                hpbb.bulletDegreeDirection = direction;
                hpbb.size = Stats.instance.currentWeapon.bulletSize;

                AudioHandler.PlayGunSounds(Stats.instance.currentWeapon.gunName.ToString());
                HollowPointSprites.StartGunAnims();
                HollowPointSprites.StartFlash();
                HollowPointSprites.StartMuzzleFlash(OrientationHandler.finalDegreeDirection, 1);

                Destroy(bullet, Stats.instance.current_bulletLifetime);

                yield return new WaitForSeconds(0.07f);
                isFiring = false;

                if (h_state.dashing) break;
            }
            HeatHandler.IncreaseHeat(Stats.instance.current_heatPerShot);

            isFiring = false;
        }

        public IEnumerator SpreadShot(int pellets)
        {
            slowWalkDisableTimer = 15f;
            GameCameras.instance.cameraShakeFSM.SendEvent("SmallShake"); //SmallShake
            HollowPointSprites.StartGunAnims();
            HollowPointSprites.StartFlash();
            HollowPointSprites.StartMuzzleFlash(OrientationHandler.finalDegreeDirection, 1);
            AudioHandler.PlayGunSounds("Shotgun");

            float direction = OrientationHandler.finalDegreeDirection; //90 degrees
            DirectionalOrientation orientation = OrientationHandler.directionOrientation; 

            float coneDegree = 40;
            float angleToSpawnBullet = direction - (coneDegree / 2); //90 - (30 / 2) = 75, start at 75 degrees
            float angleIncreasePerPellet = coneDegree / (pellets + 2); // 30 / (5 + 2) = 4.3, move angle to fire for every pellet by 4.3 degrees

            angleToSpawnBullet = angleToSpawnBullet + angleIncreasePerPellet;

            //Checks if the player is firing upwards, and enables the x offset so the bullets spawns directly ontop of the knight
            //from the gun's barrel instead of spawning to the upper right/left of them 
            bool fixYOrientation = (direction == 270 || direction == 90) ? true : false;
            for (int i = 0; i < pellets; i++)
            {
                yield return new WaitForEndOfFrame();

                GameObject bullet = HollowPointPrefabs.SpawnBulletFromKnight(angleToSpawnBullet, orientation);
                BulletBehaviour hpbb = bullet.GetComponent<BulletBehaviour>();
                hpbb.bulletDegreeDirection += UnityEngine.Random.Range(-3, 3);
                //hpbb.pierce = PlayerData.instance.equippedCharm_13;
                bullet.transform.localScale = new Vector3(0.2f,0.2f,0.1f);

                angleToSpawnBullet += angleIncreasePerPellet;
                Destroy(bullet, 0.7f);
            }

            yield return new WaitForSeconds(0.05f);
            isFiring = false;
        }

        public IEnumerator FireGAU()
        {
            isFiring = true;
            GameCameras.instance.cameraShakeFSM.SendEvent("EnemyKillShake");
            //float direction = (hc_instance.cState.facingRight) ? 315 : 225;
            //DirectionalOrientation orientation = DirectionalOrientation.Diagonal;
            float direction = OrientationHandler.finalDegreeDirection;
            DirectionalOrientation orientation = OrientationHandler.directionOrientation;

            AudioHandler.PlayGunSounds("gatlinggun", 1f);
            for (int b = 0; b < 14; b++)
            {
                GameObject bullet = HollowPointPrefabs.SpawnBulletFromKnight(direction, orientation);
                HeatHandler.IncreaseHeat(1.5f);
                BulletBehaviour hpbb = bullet.GetComponent<BulletBehaviour>();
                hpbb.bulletOriginPosition = bullet.transform.position; //set the origin position of where the bullet was spawned
                hpbb.specialAttrib = "DungExplosionSmall";
                hpbb.bulletSpeedMult += 1.5f;

                HollowPointSprites.StartGunAnims();
                HollowPointSprites.StartFlash();
                HollowPointSprites.StartMuzzleFlash(OrientationHandler.finalDegreeDirection, 1);

                Destroy(bullet, 1f);
                yield return new WaitForSeconds(0.03f); //0.12f This yield will determine the time inbetween shots   
            }

            yield return new WaitForSeconds(0.02f);
            isFiring = false;
        }

        public IEnumerator KnockbackRecoil(float recoilStrength, float applyForceFromDegree)
        {
            if (recoilStrength < 0.05) yield break;
            float deg = applyForceFromDegree + 180;
            deg = deg % 360;

            float radian = deg * Mathf.Deg2Rad;
            float xDeg = (float) ((1 * recoilStrength) * Math.Cos(radian));
            float yDeg = (float) ((1 * recoilStrength) * Math.Sin(radian));

            xDeg = (xDeg == 0) ? 0 : xDeg;
            yDeg = (yDeg == 0) ? 0 : yDeg;

            HeroController.instance.cState.shroomBouncing = true;
            HeroController.instance.cState.recoiling = true;

            if (deg == 90 || deg == 270)
            {
                knight.velocity = new Vector2(0, yDeg);
                yield break;
            }

            if (HeroController.instance.cState.facingRight)
            {
                //Modding.Logger.Log(HeroController.instance.GetAttr<float>("RECOIL_HOR_VELOCITY"));
                HeroController.instance.SetAttr<int>("recoilSteps", 0);
                HeroController.instance.cState.recoilingLeft = true;
                HeroController.instance.cState.recoilingRight = false;
                HeroController.instance.SetAttr<bool>("recoilLarge", true);

                knight.velocity = new Vector2(-xDeg, yDeg);
            }
            else
            {
                //Modding.Logger.Log(HeroController.instance.GetAttr<float>("RECOIL_HOR_VELOCITY"));
                HeroController.instance.SetAttr<int>("recoilSteps", 0);
                HeroController.instance.cState.recoilingLeft = false;
                HeroController.instance.cState.recoilingRight = true;
                HeroController.instance.SetAttr<bool>("recoilLarge", true);

                knight.velocity = new Vector2(xDeg, yDeg);
            }

            yield return null;
        }

        public void OnDestroy()
        {
            On.NailSlash.StartSlash -= OnSlash;
            Destroy(gameObject.GetComponent<AttackHandler>());
        }
    }
}
