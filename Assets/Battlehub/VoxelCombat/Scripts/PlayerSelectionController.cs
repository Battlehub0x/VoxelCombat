﻿using cakeslice;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Battlehub.VoxelCombat
{
 
    public interface IPlayerSelectionController
    {
       
    }

    public class PlayerSelectionController : MonoBehaviour, IPlayerSelectionController
    {
        [SerializeField]
        private GameViewport m_viewport;

        [SerializeField]
        private Color m_ownColor = Color.green;
        [SerializeField]
        private Color m_enemyColor = Color.red;
        [SerializeField]
        private Color m_neutralColor = Color.black;

        private OutlineEffect m_outlineEffect;
        private IPlayerCameraController m_cameraController;

        private int m_localPlayerIndex = -1;
        private int LocalPlayerIndex 
        {
            get { return m_localPlayerIndex; }
            set
            {
                if (m_localPlayerIndex != value)
                {
                    m_localPlayerIndex = value;
                    if (m_viewport != null) //if start method was called
                    {
                        if(m_outlineEffect != null)
                        {
                            Destroy(m_outlineEffect);
                        }
                        
                        m_cameraController = Dependencies.GameView.GetCameraController(LocalPlayerIndex);

                        m_outlineEffect = m_viewport.Camera.GetComponent<OutlineEffect>();
                        if(m_outlineEffect == null)
                        {
                            m_outlineEffect = m_viewport.Camera.gameObject.AddComponent<OutlineEffect>();
                        }
                        InitializeOutlineEffect();
                    }
                }
            }
        }

        private int PlayerIndex
        {
            get
            {
                Guid playerId = m_gameState.GetLocalPlayerId(LocalPlayerIndex);
                return m_gameState.GetPlayerIndex(playerId);
            }
        }

        private IUnitSelection m_unitSelection;
        private IUnitSelection m_targetSelection;
        private IVoxelInputManager m_inputManager;
        private IVoxelGame m_gameState;
        private IVoxelMap m_map;
        private IBoxSelector m_boxSelector;

        private readonly HashSet<long> m_wasSelected = new HashSet<long>();
        private MapPos m_mapCursor = new MapPos(-1, -1);
        private float m_selectInterval;
        private float m_unselectInterval;
        private bool m_multiselectMode;

        private void InitializeOutlineEffect()
        {
            m_outlineEffect.fillAmount = 0;
            m_outlineEffect.lineThickness = 2.0f;
            m_outlineEffect.lineIntensity = 10;
            m_outlineEffect.scaleWithScreenSize = false;
            m_outlineEffect.lineColor0 = m_ownColor;
            m_outlineEffect.lineColor1 = m_enemyColor;
            m_outlineEffect.lineColor2 = m_neutralColor;
        }

        private void Awake()
        {
            m_unitSelection = Dependencies.UnitSelection;
            m_targetSelection = Dependencies.TargetSelection;
            m_inputManager = Dependencies.InputManager;
            m_gameState = Dependencies.GameState;
            m_map = Dependencies.Map;
        }

        private void Start()
        {
            LocalPlayerIndex = m_viewport.LocalPlayerIndex;
            m_cameraController = Dependencies.GameView.GetCameraController(LocalPlayerIndex);

            if(m_inputManager.IsKeyboardAndMouse(LocalPlayerIndex))
            {
                m_boxSelector = Dependencies.GameView.GetBoxSelector(LocalPlayerIndex);
                m_boxSelector.Filtering += OnBoxSelectionFiltering;
                m_boxSelector.Selected += OnBoxSelection;
            }
        }

        private void OnDestroy()
        {
            if(m_outlineEffect != null)
            {
                Destroy(m_outlineEffect);
            }

            if(m_boxSelector != null)
            {
                m_boxSelector.Filtering -= OnBoxSelectionFiltering;
                m_boxSelector.Selected -= OnBoxSelection;
            }
        }

        private void Update()
        {
            if (m_gameState.IsActionsMenuOpened(LocalPlayerIndex))
            {
                if (!m_cameraController.IsInputEnabled)
                {
                    m_cameraController.IsInputEnabled = true;
                }
                return;
            }

            if (m_gameState.IsMenuOpened(LocalPlayerIndex))
            {
                if(!m_cameraController.IsInputEnabled)
                {
                    m_cameraController.IsInputEnabled = true;
                }
                return;
            }

            if (m_gameState.IsContextActionInProgress(LocalPlayerIndex))
            {
                if(!m_cameraController.IsInputEnabled)
                {
                    m_cameraController.IsInputEnabled = true;
                }
                return;
            }

            if (m_gameState.IsPaused || m_gameState.IsPauseStateChanging)
            {
                return;
            }

            if(m_gameState.IsReplay)
            {
                return;
            }

            int playerIndex = PlayerIndex;

            m_selectInterval -= Time.deltaTime;
            m_unselectInterval -= Time.deltaTime;

            bool multiselect = m_inputManager.GetButton(InputAction.RB, LocalPlayerIndex);
            if(m_inputManager.GetButtonDown(InputAction.LMB, LocalPlayerIndex))
            {
                RaycastHit hitInfo;
                if(Physics.Raycast(m_cameraController.Ray, out hitInfo))
                {
                    Voxel voxel = hitInfo.transform.GetComponentInParent<Voxel>();
                    if(voxel != null)
                    {
                        VoxelData data = voxel.VoxelData;
                        if (VoxelData.IsControllableUnit(data.Type) && data.Owner == playerIndex)
                        {
                            m_unitSelection.ClearSelection(playerIndex);
                            m_unitSelection.Select(playerIndex, playerIndex, new[] { data.UnitOrAssetIndex });
                        }
                        else
                        {
                            m_unitSelection.ClearSelection(playerIndex);
                        }
                    }
                    else
                    {
                        m_unitSelection.ClearSelection(playerIndex);
                    }
                }
                else
                {
                    m_unitSelection.ClearSelection(playerIndex);
                }

                if(m_boxSelector != null)
                {
                    m_boxSelector.Activate();
                }
            }
            
            if (m_inputManager.GetButtonDown(InputAction.LB, LocalPlayerIndex))
            {
                bool select = true;
                
                if(multiselect)
                {
                    long[] units = m_gameState.GetUnits(playerIndex).ToArray();
                    long unitIndex = GetAt(units, m_cameraController.MapCursor);

                    if(m_unitSelection.IsSelected(playerIndex, playerIndex, unitIndex))
                    {
                        m_unitSelection.Unselect(playerIndex, playerIndex, new[] { unitIndex });
                        m_wasSelected.Remove(unitIndex);
                        select = false;
                    }
                }
                
                if(select)
                {
                    Select(playerIndex, multiselect);
                    m_selectInterval = 0.3f;
                    m_unselectInterval = float.PositiveInfinity;
                    m_multiselectMode = true;
                }
                else
                {
                    m_selectInterval = float.PositiveInfinity;
                    m_unselectInterval = 0.3f;
                }
                
            }
            else if(m_inputManager.GetButton(InputAction.LB, LocalPlayerIndex))
            {
                if (m_selectInterval <= 0)
                {
                    Select(playerIndex, multiselect);
                    m_selectInterval = 0.2f;
                }
                
                if(m_unselectInterval <= 0)
                {
                    Unselect(playerIndex);
                    m_unselectInterval = 0.2f;
                }

                m_cameraController.IsInputEnabled = false;
            }            
            else if(m_inputManager.GetButtonUp(InputAction.LB, LocalPlayerIndex, false, false))
            {
                m_cameraController.IsInputEnabled = true;
            }

            if (m_inputManager.GetButtonDown(InputAction.RB, LocalPlayerIndex))
            {
                m_multiselectMode = false;
            }
            else if (m_inputManager.GetButtonUp(InputAction.RB, LocalPlayerIndex))
            {
                if(!m_multiselectMode)
                {
                    if(!m_targetSelection.HasSelected(playerIndex))
                    {
                        m_wasSelected.Clear();
                        m_unitSelection.ClearSelection(playerIndex);
                    }
                }
            }   
        }

        private void OnBoxSelectionFiltering(object sender, FilteringArgs e)
        {
            GameObject go = e.Object;
            Voxel voxel = go.GetComponentInParent<Voxel>();
            if(voxel == null || !VoxelData.IsControllableUnit(voxel.Type) || voxel.Owner != PlayerIndex)
            {
                e.Cancel = true;
            }
        }

        private void OnBoxSelection(object sender, BoxSelectEventArgs e)
        {
            long[] selection = e.Result.Select(go => go.GetComponentInParent<Voxel>().VoxelData.UnitOrAssetIndex).ToArray();
            m_unitSelection.Select(PlayerIndex, PlayerIndex, selection);
        }

        private void Unselect(int playerIndex)
        {
            long selectedIndex = -1;
            long[] selection = m_unitSelection.GetSelection(playerIndex, playerIndex);
            if (selection.Length > 0)
            {
                selectedIndex = selection[0];
            }

            VoxelData unitData = FindClosestTo(PlayerIndex, selectedIndex, selection, false);
            if (unitData != null)
            {
                m_unitSelection.Unselect(playerIndex, playerIndex, new[] { unitData.UnitOrAssetIndex });
                m_wasSelected.Remove(unitData.UnitOrAssetIndex);
            }
        }

        private void Select(int playerIndex, bool multiselect)
        {
            long[] units = m_gameState.GetUnits(playerIndex).ToArray();

            long selectedIndex = -1;
            long[] selection = m_unitSelection.GetSelection(playerIndex, playerIndex);
            if (selection.Length > 0)
            {
                selectedIndex = selection[0];
            }

            if(!multiselect)
            {
                if (m_mapCursor != m_cameraController.MapCursor)
                {
                    m_wasSelected.Clear();
                }
            }
            
            VoxelData unitData = FindClosestTo(PlayerIndex, selectedIndex, units, false);
            if (unitData != null)
            {
                if (m_wasSelected.Count == 0)
                {
                    m_unitSelection.Select(playerIndex, playerIndex, new[] { unitData.UnitOrAssetIndex });
                }
                else
                {
                    m_unitSelection.AddToSelection(playerIndex, playerIndex, new[] { unitData.UnitOrAssetIndex });
                }

                m_wasSelected.Add(unitData.UnitOrAssetIndex);

                if (m_wasSelected.Count == 1)
                {
                    IVoxelDataController dc = m_gameState.GetVoxelDataController(playerIndex, unitData.UnitOrAssetIndex);
                    Coordinate coord = dc.Coordinate;
                    coord = coord.ToWeight(m_cameraController.Weight);
                    coord.Altitude += dc.ControlledData.Height;

                    m_cameraController.MapPivot = coord.MapPos;
                    m_cameraController.SetVirtualMousePosition(coord, true, false);
                    
                    m_mapCursor = m_cameraController.MapCursor;
                }
            }
        }

        private VoxelData FindClosestTo(int playerIndex, long selectedIndex, long[] units, bool unselectMode)
        {
            MapPos mapCursor = m_cameraController.MapCursor;
            Vector3 selectedPosition;
            if (selectedIndex == -1 || m_mapCursor != mapCursor)
            {
                selectedPosition = m_cameraController.Cursor;
            }
            else
            {
                selectedPosition = GetUnitPosition(playerIndex, selectedIndex);
            }

            float minDistance = float.PositiveInfinity;
            long closestIndex = -1;
            VoxelData closestVoxelData = null;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(m_viewport.Camera);

            for (int i = 0; i < units.Length; ++i)
            {
                long unitIndex = units[i];

                if(unselectMode)
                {
                    if (!m_wasSelected.Contains(unitIndex))
                    {
                        continue;
                    }
                }
                else
                {
                    if (m_wasSelected.Contains(unitIndex))
                    {
                        continue;
                    }

                    if (unitIndex == selectedIndex && m_mapCursor != mapCursor)
                    {
                        continue;
                    }
                }

                IVoxelDataController controller = m_gameState.GetVoxelDataController(playerIndex, unitIndex);
                Vector3 position = m_map.GetWorldPosition(controller.Coordinate);
                if (IsVisible(planes, controller.ControlledData.VoxelRef) && VoxelData.IsControllableUnit(controller.ControlledData.Type))
                {
                    Vector3 toVector = (position - selectedPosition);
                    
                    float distance = toVector.sqrMagnitude;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestIndex = unitIndex;
                        closestVoxelData = controller.ControlledData;
                    }
                }
            }

            return closestVoxelData;
        }

        private long GetAt(long[] units, MapPos position)
        {
            int playerIndex = PlayerIndex;

            for (int i = 0; i < units.Length; ++i)
            {
                long unitIndex = units[i];

                IVoxelDataController controller = m_gameState.GetVoxelDataController(playerIndex, unitIndex);
                if(controller.Coordinate.MapPos == new Coordinate(position, m_cameraController.Weight, 0).ToWeight(controller.Coordinate.Weight).MapPos)
                {
                    return unitIndex;
                }
            }

            return -1;
        }


        private bool IsVisible(Plane[] planes, Voxel voxel)
        {
            if (voxel == null)
            {
                return false;
            }

            Bounds bounds = voxel.Renderer.bounds;
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        private Vector3 GetUnitPosition(int playerIndex, long unitIndex)
        {
            IVoxelDataController controller = m_gameState.GetVoxelDataController(playerIndex, unitIndex);
            Vector3 position = m_map.GetWorldPosition(controller.Coordinate);
            return position;
        }
    }

}
