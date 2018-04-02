﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using ProtoBuf;
using UnityEngine;

namespace Battlehub.VoxelCombat
{
    public delegate void VoxelDataEvent<T>(T payload);

    public enum VoxelDataState
    {
        Idle,
        SearchingPath,
        Moving,
        Dead,
        Busy,
    }

    public class VoxelDataCellPair
    {
        public VoxelData Data;
        public MapCell Cell;

        public VoxelDataCellPair(VoxelData data, MapCell cell)
        {
            Data = data;
            Cell = cell;
        }
    }


    [ProtoContract]
    public class VoxelData
    {

#if !SERVER
        public event VoxelDataEvent<Voxel> VoxelRefSet;
        public event VoxelDataEvent<Voxel> VoxelRefReset;

        //Do not Serialize
        private Voxel m_voxelRef;
        public Voxel VoxelRef
        {
            get { return m_voxelRef; }
            set
            {
                if(value == null)
                {
                    if(m_voxelRef != null)
                    {
                        if (VoxelRefReset != null)
                        {
                            VoxelRefReset(m_voxelRef);
                        }
                        m_voxelRef = null;
                    }
                }
                else
                {
                    if(m_voxelRef != null)
                    {
                        throw new InvalidOperationException("VoxelRef already set");
                    }

                    m_voxelRef = value;
                    if(VoxelRefSet != null)
                    {
                        VoxelRefSet(m_voxelRef);
                    }
                }
            }
        }
#endif

        [ProtoMember(1)]
        public VoxelData Next;


        [ProtoMember(4)]
        public int Weight;

        [ProtoMember(5)]
        public int Height_Field;

        [ProtoMember(6)]
        public int Altitude;

        [ProtoMember(7)]
        public int Type;

        [ProtoMember(8)]
        public int Owner;

        [ProtoMember(9)]
        public int Dir;
        //
        //         2 -ROW
        //           |
        // 1 -Col <-   -> 3 +COL
        //           |
        //         0 +ROW
        [ProtoMember(10)]
        public int Health;

        [ProtoMember(11)]
        public VoxelDataState State;

        public long UnitOrAssetIndex = -1;

        public bool IsAlive
        {
            get { return Health > 0; }
        }

        public bool IsNeutral
        {
            get { return Owner == 0; }
        }

        public int RealHeight
        {
            get
            {
                if(IsCollapsed)
                {
                    return Height_Field >> 16;
                }
                return Height_Field;
            }
        }

        public int Height
        {
            get { return (Height_Field & 0x0000FFFF); }
            set
            {
                if(IsCollapsed)
                {
                    Debug.LogError("Unable to set height. IsCollapsed == true");
                    return;
                }

                Height_Field = value & 0x0000FFFF;
            }
        }

        public bool IsCollapsed
        {
            get { return (Height_Field & 0x7FFF0000) != 0; }
            set
            {
                bool isCollapsed = IsCollapsed;
                if(isCollapsed == value)
                {
                    return;
                }

                if (value)
                {
                    Height_Field <<= 16;
                }
                else
                {
                    Height_Field >>= 16;
                }
            }
        }

        public VoxelData GetNext(bool notCollapsed)
        {
            if(notCollapsed)
            {
                VoxelData next = Next;
                while(next != null)
                {
                    if(!next.IsCollapsed)
                    {
                        return next;
                    }

                    next = next.Next;
                }
                return null;
            }
            return Next;
        }


    
        public VoxelData()
        {
        }

        public VoxelData(VoxelData data)
        {
            Next = data.Next;
            Weight = data.Weight;
            Height = data.Height;
            Altitude = data.Altitude;
            Type = data.Type;
            Owner = data.Owner;
            Dir = data.Dir;
            Health = data.Health;
            State = data.State;
        }

        public static int RotateRight(int dir)
        {
            dir++;
            if(dir > 3)
            {
                dir = 0;
            }
            return dir;
        }

        public static int RotateLeft(int dir)
        {
            dir--;
            if(dir < 0)
            {
                dir = 3;
            }
            return dir;
        }


        //CW
        public static int ShouldRotateRight(int dir, Coordinate from, Coordinate to)
        {
            int deltaCol = to.Col - from.Col;
            int deltaRow = to.Row - from.Row;
            if(dir == 0)
            {
                if(deltaRow < 0)
                {
                    return 2;
                }

                if(deltaCol > 0)
                {
                    return 1;
                }
            }
            else if(dir == 1)
            {
                if (deltaCol < 0)
                {
                    return 2;
                }
                if (deltaRow < 0)
                {
                    return 1;
                }
            }
            else if(dir == 2)
            {
                if(deltaCol < 0)
                {
                    return 1;
                }
            }
            else if(dir == 3)
            {
                if(deltaRow > 0)
                {
                    return 1;
                }
            }

            return 0;
        }

        //CCW
        public static int ShouldRotateLeft(int dir, Coordinate from, Coordinate to)
        {
            int deltaCol = to.Col - from.Col;
            int deltaRow = to.Row - from.Row;
            if (dir == 0)
            {
                if (deltaCol < 0)
                {
                    return 1;
                }
            }
            else if (dir == 1)
            {
                if (deltaRow > 0)
                {
                    return 1;
                }

            }
            else if (dir == 2)
            {
                if(deltaRow > 0)
                {
                    return 2;
                }

                if (deltaCol > 0)
                {
                    return 1;
                }
            }
            else if (dir == 3)
            {
                if (deltaCol > 0)
                {
                    return 2;
                }

                if(deltaRow < 0)
                {
                    return 1;
                }
            }

            return 0;
        }

        public bool IsEatableBy(int type, int weight, int height, int altitude, int owner)
        {
            if(type != (int)KnownVoxelTypes.Eater)
            {
                return false;
            }

            return Height != 0 && //Voxel which will be eaten should have non-zero height
                height + altitude > Altitude && //Eater altitude + height should be greater that voxel altitude
                Weight < weight && //Eater should have greater weight
                Type != (int)KnownVoxelTypes.Ground && //Voxel should not be ground
                (
                    Owner != owner ||
                    Type == (int)KnownVoxelTypes.Eatable
                 );   
        }

        public bool IsAttackableBy(VoxelData voxelData)
        {
            int type = voxelData.Type;
            int weight = voxelData.Weight;
            int owner = voxelData.Owner;
            if (type != (int)KnownVoxelTypes.Eater && type != (int)KnownVoxelTypes.Bomb)
            {
                return false;
            }

            bool result = Height != 0 &&
                          Weight < weight &&
                          Weight >= weight - 2 &&
                          !voxelData.IsExplodableBy(Type, Weight);
            if(result)
            {
                if(type == (int)KnownVoxelTypes.Bomb)
                {
                    if(IsNeutral)
                    {
                        return false;
                    }

                    if(Owner != owner)
                    {
                        return true;
                    }
                }
                else
                {
                    if(IsNeutral && Type != (int)KnownVoxelTypes.Eatable)
                    {
                        return false;
                    }

                    if (Owner != owner || Type == (int)KnownVoxelTypes.Eatable)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return result;
        }

        public static bool IsStatic(int type)
        {
            return type == (int)KnownVoxelTypes.Ground;
        }

        public static bool IsUnit(int type)
        {
            return 
                type == (int)KnownVoxelTypes.Eater ||
                type == (int)KnownVoxelTypes.Bomb ||
                type == (int)KnownVoxelTypes.Spawner;
        }
        public static bool IsControllableUnit(int type)
        {
            return
                type == (int)KnownVoxelTypes.Eater ||
                type == (int)KnownVoxelTypes.Bomb;                
        }


        /// <summary>
        /// This method is used to determine wheter object of type could be located on top of this Voxel
        /// </summary>
        /// <param name="type"></param>
        /// <param name="weight"></param>
        /// <returns></returns>
        public bool IsBaseFor(int type, int weight)
        {
            if(Type == (int)KnownVoxelTypes.Ground)
            {
                return Weight >= weight;
            }
            else if (Type == (int)KnownVoxelTypes.Bomb)
            {
                if(type == (int)KnownVoxelTypes.Bomb ||
                   type == (int)KnownVoxelTypes.Eater)
                {
                    return Weight == weight;
                }
            }
            else if (Type == (int)KnownVoxelTypes.Eater)
            {
                if (type == (int)KnownVoxelTypes.Bomb ||
                   type == (int)KnownVoxelTypes.Eater)
                {
                    return Weight == weight;
                }
            }
            else if(Type == (int)KnownVoxelTypes.Eatable)
            {
                if (type == (int)KnownVoxelTypes.Bomb ||
                    type == (int)KnownVoxelTypes.Eater)
                {
                    return Weight == weight;
                }
                else if(type == (int)KnownVoxelTypes.Eatable)
                {
                    return Weight >= weight;
                }
            }
            else if(Type == (int)KnownVoxelTypes.Spawner)
            {
                if (type == (int)KnownVoxelTypes.Bomb ||
                    type == (int)KnownVoxelTypes.Eater)
                {
                    return Weight >= weight;
                }
            }

            return false;
        }

        public bool IsTargetFor(int type, int weight, int playerIndex)
        {
            return IsExplodableBy(type, weight) || IsCollapsableBy(type, weight) || Weight < weight && Owner != playerIndex;
        }

        public bool IsExplodableBy(int type, int weight)
        {
            if(type != (int)KnownVoxelTypes.Bomb)
            {
                return false;
            }

            if(IsNeutral)
            {
                return false;
            }

            int w = weight;
            for(int i = 0; i <= GameConstants.ExplodableWeightDelta; ++i)
            {
                if(w == Weight)
                {
                    return true;
                }
                w++;
            }

            return false;
        }

        public bool IsCollapsableBy(int type, int weight)
        {
            if (Type == (int)KnownVoxelTypes.Bomb  ||
                Type == (int)KnownVoxelTypes.Eater)
            {
                if (type == (int)KnownVoxelTypes.Bomb ||
                    type == (int)KnownVoxelTypes.Eater)
                {
                    return Weight == weight;
                }
            }
            return false;
        }

        public VoxelData GetLast()
        {
            VoxelData current = this;
            while(current.Next != null)
            {
                current = current.Next;
            }
            return current;
        }

        public VoxelData GetPenultimate()
        {
            VoxelData current = this;
            VoxelData previous = null;
            while (current.Next != null)
            {
                previous = current;
                current = current.Next;
            }
            return previous;
        }

        public VoxelData GetPrevious(VoxelData voxel)
        {
            VoxelData current = this;
            VoxelData previous = null;
            while (current.Next != null && current != voxel)
            {
                previous = current;
                current = current.Next;
            }
            return previous;
        }


        public VoxelData GetLastNeturalOfType(int type)
        {
            VoxelData result = null;
            VoxelData data = this;
            while (data != null)
            {
                if (data.Type == type && data.IsNeutral)
                {
                    result = data;
                }
                data = data.Next;
            }
            return result;
        }

        public VoxelData GetLastOfType(int type)
        {
            VoxelData result = null;
            VoxelData data = this;
            while (data != null)
            {
                if (data.Type == type)
                {
                    result = data;
                }
                data = data.Next;
            }
            return result;
        }

        public VoxelData GetLastStatic()
        {
            VoxelData result = null;
            VoxelData data = this;
            while (data != null)
            {
                if (IsStatic(data.Type))
                {
                    result = data;
                }
                data = data.Next;
            }
            return result;
        }


        public VoxelData GetLastSelectable()
        {
            VoxelData result = null;
            VoxelData data = this;
            while (data != null)
            {
                if (IsControllableUnit(data.Type))
                {
                    result = data;
                }
                data = data.Next;
            }
            return result;
        }

        public VoxelData GetFirstOfType(int type)
        {
            if (Type == type)
            {
                return this;
            }

            VoxelData data = Next;
            while (data != null)
            {
                if (data.Type == type)
                {
                    return data;
                }
                data = data.Next;
            }
            return null;
        }

        public void Append(VoxelData appendData)
        {
            VoxelData data = this;
            while (data != null)
            {
                if (data.Next == null)
                {
                    data.Next = appendData;
                    break;
                }
                data = data.Next;
            }
        }

        public override string ToString()
        {
            return string.Format("VoxelData A:{0} W:{1} T:{2} O:{3}", Altitude, Weight, Type, Owner);
        }

    }

    [ProtoContract]
    public class MapCell
    {
        [ProtoMember(1)]
        public VoxelData VoxelData;

        public MapCell Parent;

        [ProtoMember(2)]
        public int Index;

        [ProtoMember(3)]
        public MapCell[] Children;

        public int Usages;

        public MapPos GetPosition()
        {
            MapCell parent = this;
            int row = 0;
            int col = 0;

            int multiplier = 1;
            while (parent != null)
            {
                row += (parent.Index / 2) * multiplier;
                col += (parent.Index % 2) * multiplier;

                parent = parent.Parent;
                multiplier *= 2;
            }

            return new MapPos(row, col);
        }

        public int GetTotalHeight()
        {
            MapCell cell = this;
            while(cell != null)
            {
                if (cell.VoxelData != null)
                {
                    VoxelData voxelData = cell.VoxelData.GetLast();
                    if (voxelData != null && !voxelData.IsCollapsed)
                    {
                        return voxelData.Altitude + voxelData.Height;
                    }
                }

                cell = cell.Parent;
            }
            return 0;
        }

        public int GetTotalHeight(int type)
        {
            MapCell cell = this;
            while (cell != null)
            {
                if (cell.VoxelData != null)
                {
                    VoxelData voxelData = cell.VoxelData.GetLastOfType(type);
                    if(voxelData != null && !voxelData.IsCollapsed)
                    {
                        return voxelData.Altitude + voxelData.Height;
                    }
                }

                cell = cell.Parent;
            }
            return 0;
        }

       



        /// <summary>
        /// This method will not return VoxelData of zero height
        /// </summary>
        /// <param name="altitude"></param>
        /// <returns></returns>
        public VoxelData GetVoxelDataAt(int altitude)
        {
            VoxelData next = VoxelData;
            while(next != null)
            {
                if(next.Altitude == altitude && next.Height > 0)
                {
                    return next;
                }
                next = next.Next;
            }
            return null;
        }


        public void AppendVoxelData(VoxelData voxelData)
        {
            if(VoxelData == null)
            {
                VoxelData = voxelData;
            }
            else
            {

                VoxelData.Append(voxelData);
            }
        }

        public void DestroyVoxelData(VoxelData data)
        {
            //Debug.Assert(data.VoxelRef == null);
           

            int height = data.Height;
            VoxelData next = data.Next;

            DecreaseHeight(height, next);

            Remove(data);

            ForEachDescendant(descendant =>
            {
                if (descendant.VoxelData != null)
                {
                    DecreaseHeight(height, descendant.VoxelData);
                }
            });


            if (VoxelData == data)
            {
                VoxelData = data.Next;
            }

        }

        private void Remove(VoxelData voxelData)
        {
            VoxelData data = VoxelData;
            VoxelData prev = null;
            do
            {
                if (data.Altitude == voxelData.Altitude)
                {
                    Debug.Assert(data.Type == voxelData.Type);
                    Debug.Assert(data.Height == voxelData.Height);

                    if (prev != null)
                    {
                        prev.Next = data.Next;
                    }

                    break;
                }

                prev = data;
                data = data.Next;
            }
            while (data != null);
        }

        private static void DecreaseHeight(int height, VoxelData next)
        {
            while (next != null)
            {
                next.Altitude -= height;
#if !SERVER
                if (next.VoxelRef != null)
                {
                    next.VoxelRef.Altitude = next.Altitude;
                }
#endif
                next = next.Next;
            }
        }

        public VoxelData GetById(long unitId)
        {
            MapCell cell = this;
            while(cell != null)
            {
                VoxelData data = cell.VoxelData;
                while(data != null)
                {
                    if(data.UnitOrAssetIndex == unitId)
                    {
                        return data;
                    }

                    data = data.Next;
                }

                cell = cell.Parent;
            }

            return null;
        }

        /// <summary>
        /// This method will return target for voxel of type and weight
        /// </summary>
        /// <param name="type">type of voxel which will be moved</param>
        /// <param name="weight">weight of voxel which will be moved</param>
        /// <param name="target">voxel data which will be collapsed or destroyed</param>
        /// <returns>voxel data on top of which voxel should be located</returns>
        public VoxelData GetDefaultTargetFor(int type, int weight, int playerIndex, bool lowestPossible, out VoxelData target, params int[] exceptOwners)
        {
            target = null;

            MapCell cell = this;
            VoxelData nonDestroyable = null;
            while (true)
            {
                VoxelData beneath = null;
           
                if (cell.VoxelData != null  /* && !cell.VoxelData.HasWeightLessThen(weight)*/)
                {
                    VoxelData data = cell.VoxelData;
                    while (data != null)
                    {
                        if (!data.IsCollapsed)
                        {
                            bool foundNewTarget = data.IsTargetFor(type, weight, playerIndex) && Array.IndexOf(exceptOwners, data.Owner) == -1;

                            if (lowestPossible)
                            {
                                if(!foundNewTarget)
                                {
                                    if (!data.IsCollapsableBy(type, weight) && data.IsBaseFor(type, weight))
                                    {
                                        beneath = data;
                                    }
                                    else
                                    {
                                        nonDestroyable = data;
                                    }
                                }
                                else
                                {
                                    if(target != null)
                                    {
                                        if (!target.IsCollapsableBy(type, weight) && target.IsBaseFor(type, weight))
                                        {
                                            beneath = target;
                                        }
                                        else
                                        {
                                            nonDestroyable = target;
                                        }
                                    }   
                                }
                            }
                            else
                            {
                                if (!data.IsCollapsableBy(type, weight) && data.IsBaseFor(type, weight))
                                {
                                    beneath = data;
                                }
                                else
                                {
                                    nonDestroyable = data;
                                }
                            }
                            
                            

                            if (foundNewTarget)
                            {
                                target = data;
                                if (lowestPossible && beneath != null)
                                {
                                    if (target == beneath)
                                    {
                                        beneath = null;
                                    }
                                    break;
                                }
                            }
                        }
                        data = data.Next;
                    }
                }

                if(beneath != null)
                {
                    if(nonDestroyable != null && nonDestroyable != target && beneath.Altitude < nonDestroyable.Altitude)
                    {
                        return null;
                    }

                    return beneath;
                }
                
                cell = cell.Parent;
                if (cell == null)
                {
                    return beneath;
                }
            }
        }

        public VoxelData GetPreviousFor(VoxelData data, int type, int weight, int playerIndex)
        {
            VoxelData target;
            VoxelData beneath = GetDefaultTargetFor(type, weight, playerIndex, true, out target);
            if(beneath == null)
            {
                return null;
            }

            if(target == null)
            {
                return beneath;
                //return null;
            }

            VoxelData previous = null;
            while(target != data && target != null)
            {
                previous = target;
                target = target.Next;
            }

            if(previous == null)
            {
                previous = beneath;
            }

            if(!previous.IsBaseFor(type, weight))
            {
                return null;
            }

            return previous;

        }


        public bool HasVoxelData(VoxelData hasData)
        {
            VoxelData voxelData = VoxelData;
            while(voxelData != null)
            {
                if(voxelData == hasData)
                {
                    return true;
                }

                voxelData = voxelData.Next;
            }
            return false;
        }


        public VoxelData GetVoxelData(Func<VoxelData, bool> predicate)
        {
            VoxelData voxelData = VoxelData;
            while (voxelData != null)
            {
                if (predicate(voxelData))
                {
                    return voxelData;
                }

                voxelData = voxelData.Next;
            }
            return null;
        }

        public bool HasDescendantsWithVoxelData()
        {
            return GetDescendantsWithVoxelData(this, voxelData => true) != null;
        }

        public bool HasDescendantsWithVoxelData(Func<VoxelData, bool> predicate)
        {
            return GetDescendantsWithVoxelData(this, predicate) != null;
        }

        public VoxelData GetDescendantsWithVoxelData(Func<VoxelData, bool> predicate)
        {
            return GetDescendantsWithVoxelData(this, predicate);
        }

        public static VoxelData GetDescendantsWithVoxelData(MapCell cell, Func<VoxelData, bool> predicate)
        {
            if (cell.Children != null)
            {
                for (int i = 0; i < cell.Children.Length; ++i)
                {
                    MapCell child = cell.Children[i];
                    if (child.VoxelData != null && predicate(child.VoxelData))
                    {
                        return child.VoxelData;
                    }

                    VoxelData result = GetDescendantsWithVoxelData(child, predicate);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }


        public void ForEach(Action<VoxelData> action)
        {
            if (VoxelData == null)
            {
                return;
            }

            VoxelData data = VoxelData;
            while (data != null)
            {
                action(data);
                data = data.Next;
            }
        }

        public bool ForEach(Func<VoxelData, bool> predicate)
        {
            if (VoxelData == null)
            {
                return true;
            }

            VoxelData data = VoxelData;
            while (data != null)
            {
                if(!predicate(data))
                {
                    return false;
                }
                data = data.Next;
            }
            return true;
        }


        public void ForEach(Action<MapCell> action)
        {
            ForEach(this, action);
        }

        public void ForEachDescendant(Action<MapCell> action)
        {
            if(Children != null)
            {
                for(int i = 0; i < Children.Length; ++i)
                {
                    ForEach(Children[i], action);
                }
            }
        }

        private static void ForEach(MapCell cell, Action<MapCell> action)
        {
            action(cell);

            if (cell.Children != null)
            {
                for (int i = 0; i < cell.Children.Length; ++i)
                {
                    ForEach(cell.Children[i], action);
                }
            }
        }
    }

    [ProtoContract]
    public class MapRoot
    {
        public readonly object SyncRoot = new object();

        [ProtoMember(1)]
        public int Weight;

        [ProtoMember(2)]
        public MapCell Root;

        public MapRoot()
        {
            Weight = 0;
            Root = new MapCell();
        }

        public MapRoot(int maxWeight)
        {
            if (maxWeight < 0)
            {
                throw new System.ArgumentOutOfRangeException("weight");
            }

            Weight = maxWeight;
            Root = new MapCell();
            AddChildren(Root, maxWeight);
        }

        [OnDeserialized]
        public void OnDeserializedMethod(StreamingContext context)
        {
            SetParent(Root, null);
        }

        private void SetParent(MapCell cell, MapCell parent)
        {
            cell.Parent = parent;
            if(cell.Children != null)
            {
                for(int i = 0; i < cell.Children.Length; ++i)
                {
                    MapCell childCell = cell.Children[i];
                    SetParent(childCell, cell);
                }
            }
        }


        private void AddChildren(MapCell cell, int weight)
        {
            if (weight == 0)
            {
                return;
            }

            cell.Children = new MapCell[4];
            for (int i = 0; i < cell.Children.Length; ++i)
            {
                MapCell childCell = new MapCell();
                AddChildren(childCell, weight - 1);

                childCell.Index = i;
                childCell.Parent = cell;
                cell.Children[i] = childCell;
            }
        }

        /// <summary>
        /// Get Map Size with weight at first level
        /// </summary>
        /// <param name="weight"></param>
        /// <returns></returns>
        private int GetMapSize(float weight)
        {
            return (int)Mathf.Pow(2, weight);
        }

        private int GetMapSize()
        {
            return (int)Mathf.Pow(2, Weight);
        }

        /// <summary>
        /// Get Map Size at level with specified weight  
        /// </summary>
        /// <param name="weight"></param>
        /// <returns></returns>
        public int GetMapSizeWith(int weight)
        {
            if (weight < 0 || weight > 15)
            {
                throw new System.ArgumentOutOfRangeException("weight");
            }
            return (int)Mathf.Pow(2, Weight - weight);
        }

 
        /// <summary>
        /// Get Map Cell at level with specified weight
        /// </summary>
        /// <param name="row">i coordinate</param>
        /// <param name="col">j coordinate</param>
        /// <param name="weight">weight</param>
        /// <returns>map cell</returns>
        public MapCell Get(int row, int col, int weight)
        {
            if (weight < 0 || weight > 15)
            {
                throw new ArgumentOutOfRangeException("weight");
            }
     
            return Get(row, col, weight, Root, Weight - 1);
        }

        public VoxelData Get(Coordinate coord)
        {
            MapCell cell = Get(coord.Row, coord.Col, coord.Weight);
            if(cell == null)
            {
                return null;
            }
            return cell.GetVoxelDataAt(coord.Altitude);
        }

        private MapCell Get(int i, int j, int withWeight, MapCell currentParent, int currentWeight)
        {
            if (withWeight == currentWeight)
            {
                return currentParent.Children[i * 2 + j];
            }

            int size = GetMapSize(currentWeight - withWeight);

            int row = i / size;
            int col = j / size;

            i %= size;
            j %= size;

            return Get(i, j, withWeight, currentParent.Children[row * 2 + col], currentWeight - 1);
        }

#if !SERVER
        public MapPos GetCellPosition(Vector3 position, int weight)
        {
            if (weight < 0)
            {
                throw new ArgumentException("weight");
            }


            int relativeMapSize = GetMapSizeWith(weight);
            float cellSize = GetMapSize(weight) * GameConstants.UnitSize;

            int row = Mathf.FloorToInt(position.z / cellSize) + relativeMapSize / 2;
            int col = Mathf.FloorToInt(position.x / cellSize) + relativeMapSize / 2;

            return new MapPos(row, col);
        }

        public Vector3 GetWorldPosition(MapPos pos, int weight, MapPos.Align rowAlign = MapPos.Align.Center, MapPos.Align colAlign = MapPos.Align.Center)
        {
            float mapSize = GetMapSize() * GameConstants.UnitSize;
            float cellSize = GetMapSize(weight) * GameConstants.UnitSize;

            Vector3 result = new Vector3(-mapSize / 2 + pos.Col * cellSize, 0, -mapSize / 2 + pos.Row * cellSize);
            switch (rowAlign)
            {
                case MapPos.Align.Center:
                    result.z += cellSize / 2;
                    break;
                case MapPos.Align.Plus:
                    result.z += cellSize;
                    break;
            }

            switch (colAlign)
            {
                case MapPos.Align.Center:
                    result.x += cellSize / 2;
                    break;
                case MapPos.Align.Plus:
                    result.x += cellSize;
                    break;
            }

            return result;
        }
#endif
        public void DestroyExtraPlayers(int playersCount)
        {
            List<VoxelData> dataToDestroy = new List<VoxelData>();
            Root.ForEach(cell =>
            {
                cell.ForEach(voxelData =>
                {
                    if (voxelData.Owner >= playersCount)
                    {
                        dataToDestroy.Add(voxelData);
                    }
                });

                for (int i = 0; i < dataToDestroy.Count; ++i)
                {
                    VoxelData data = dataToDestroy[i];
                    cell.DestroyVoxelData(data);
                }
                dataToDestroy.Clear();
            });
        }

    

    }

#if !SERVER
    [ProtoContract]
    public class MapCamera
    {
        [ProtoMember(1)]
        public int m_weight;
        [ProtoMember(2)]
        public int m_radius;
        [ProtoMember(3)]
        public int m_row;
        [ProtoMember(4)]
        public int m_col;
        [ProtoMember(5)]
        public bool m_isOn;

        public bool IsOn
        {
            get { return m_isOn; }
            set
            {
                if(m_isOn != value)
                {
                    m_isOn = value;
                    if (m_isOn)
                    {
                        TurnOn();
                    }
                    else
                    {
                        TurnOff();
                    }
                }
            }
        }

        public int Weight
        {
            get { return m_weight; }
            set
            {
                if(m_weight != value)
                {
                    if(m_isOn)
                    {
                        TurnOff();
                        m_weight = value;
                        TurnOn();
                    }
                    else
                    {
                        m_weight = value;
                    }
                }
            }
        }

        public int Radius
        {
            get { return m_radius; }
            set
            {
                if(m_radius != value)
                {
                    if(m_isOn)
                    {
                        TurnOff();
                        m_radius = value;
                        TurnOn();
                    }
                    else
                    {
                        m_radius = value;
                    }
                    
                }
            }
        }
  
        public int Row
        {
            get { return m_row; }
        }

        public int Col
        {
            get { return m_col; }
        }

        private MapRoot m_map;
        public MapRoot Map
        {
            get { return m_map; }
            set
            {
                if(m_map != value)
                {
                    if(m_isOn)
                    {
                        Debug.LogWarning("MapCamera is turned on");
                        TurnOff();
                    }

                    m_map = value;

                    if(m_map != null)
                    {
                        if(m_isOn)
                        {
                            TurnOn();
                        }
                    }
                }
            }
        }

        private IVoxelFactory m_factory;

        public MapCamera(MapRoot map, int visibleRadius, int weight)
        {
            Map = map;
            m_radius = visibleRadius;
            m_weight = weight;

            int size = Map.GetMapSizeWith(weight);
            m_row = size / 2;
            m_col = size / 2;

            m_factory = Dependencies.VoxelFactory;
        }

        public void SetCamera(int row, int col, int weight)
        {            
            if(m_isOn)
            {
                TurnOff();
            }

            m_row = row;
            m_col = col;

            if(m_isOn)
            {
                TurnOn();
            }
        }

        public void Move(int rowOffset, int colOffset)
        {
            int size = Map.GetMapSizeWith(Weight);
            if (!m_isOn)
            {
                m_row += rowOffset;
                m_col += colOffset;

                return;
            }

            if (Math.Abs(rowOffset) >= Radius || Mathf.Abs(colOffset) >= Radius)
            {
                TurnOff();

                m_row += rowOffset;
                m_col += colOffset;

                TurnOn();
            }
            else
            {
                int fromRow = Row - Radius;
                int toRow = Row + Radius;

                int fromCol = Mathf.Max(0, Col - Radius);
                int toCol = Mathf.Min(size - 1, Col + Radius);

                if (rowOffset > 0)
                {
                    for (int i = 0; i < rowOffset; i++)
                    {
                        if (fromRow >= 0 && fromRow < size)
                        {
                            for (int j = fromCol; j <= toCol; ++j)
                            {
                                MapCell cell = Map.Get(fromRow, j, Weight);
                                TurnOff(cell);
                            }
                        }

                        m_row++;

                        fromRow = Row - Radius;
                        toRow = Row + Radius;

                        if (toRow >= 0 && toRow < size)
                        {
                            for (int j = fromCol; j <= toCol; ++j)
                            {
                                MapCell cell = Map.Get(toRow, j, Weight);
                                TurnOn(cell);
                            }
                        }
                    }
                }
                else if (rowOffset < 0)
                {
                    rowOffset *= -1;
                    for (int i = 0; i < rowOffset; i++)
                    {
                        if (toRow >= 0 && toRow < size)
                        {
                            for (int j = fromCol; j <= toCol; ++j)
                            {
                                MapCell cell = Map.Get(toRow, j, Weight);
                                TurnOff(cell);
                            }
                        }

                        m_row--;

                        toRow = Row + Radius;
                        fromRow = Row - Radius;

                        if (fromRow >= 0 && fromRow < size)
                        {
                            for (int j = fromCol; j <= toCol; ++j)
                            {
                                MapCell cell = Map.Get(fromRow, j, Weight);
                                TurnOn(cell);
                            }
                        }

                    }
                }

                fromRow = Mathf.Max(0, Row - Radius);
                toRow = Mathf.Min(size - 1, Row + Radius);

                fromCol = Col - Radius;
                toCol = Col + Radius;


                if (colOffset > 0)
                {
                    for (int j = 0; j < colOffset; j++)
                    {
                        if (fromCol >= 0 && fromCol < size)
                        {
                            for (int i = fromRow; i <= toRow; ++i)
                            {
                                MapCell cell = Map.Get(i, fromCol, Weight);
                                TurnOff(cell);
                            }
                        }

                        m_col++;

                        fromCol = Col - Radius;
                        toCol = Col + Radius;

                        if (toCol >= 0 && toCol < size)
                        {
                            for (int i = fromRow; i <= toRow; ++i)
                            {
                                MapCell cell = Map.Get(i, toCol, Weight);
                                TurnOn(cell);
                            }
                        }
                    }
                }
                else if (colOffset < 0)
                {
                    colOffset *= -1;
                    for (int j = 0; j < colOffset; j++)
                    {
                        if (toCol >= 0 && toCol < size)
                        {
                            for (int i = fromRow; i <= toRow; ++i)
                            {
                                MapCell cell = Map.Get(i, toCol, Weight);
                                TurnOff(cell);
                            }
                        }

                        m_col--;

                        toCol = Col + Radius;
                        fromCol = Col - Radius;

                        if (fromCol >= 0 && fromCol < size)
                        {
                            for (int i = fromRow; i <= toRow; ++i)
                            {
                                MapCell cell = Map.Get(i, fromCol, Weight);
                                TurnOn(cell);
                            }
                        }
                    }
                }
            }
        }

        public bool IsVisible(MapPos mapPos, int weight)
        {
            if(weight > Weight)
            {
                Debug.LogWarning("weight is greater than mapCamera.Weight");
                return false;
            }

            if(weight < Weight)
            {
                mapPos.Row /= (int)Mathf.Pow(2, Weight - weight);
                mapPos.Col /= (int)Mathf.Pow(2, Weight - weight);
            }

            int fromCol = Col - Radius;
            int toCol = Col + Radius;

            int fromRow = Row - Radius;
            int toRow = Row + Radius;

            return mapPos.Row >= fromRow && mapPos.Row <= toRow && mapPos.Col >= fromCol && mapPos.Col <= toCol;
        }

        private void TurnOn()
        {
            ForEachVisibleCell(cell =>
            {
                TurnOn(cell);
            });
        }

        private void TurnOff()
        {
            ForEachVisibleCell(cell =>
            {
                TurnOff(cell);
            });
        }

        private void TurnOn(MapCell cell)
        {
            if(cell.Usages == 0)
            {
                VoxelData data = cell.VoxelData;
                while (data != null)
                {
                    if (data.VoxelRef == null)
                    {
                        MapPos mappos = cell.GetPosition();
                        Voxel voxel = m_factory.Acquire(data.Type);
                        voxel.transform.position = Map.GetWorldPosition(mappos, data.Weight);
                        voxel.ReadFrom(data);

                        data.VoxelRef = voxel;
                    }
                    data = data.Next;
                }
            }
           
            if (cell.Children != null)
            {
                for (int i = 0; i < cell.Children.Length; ++i)
                {
                    MapCell child = cell.Children[i];
                    TurnOn(child);
                }
            }

            cell.Usages++;
        }

        private void TurnOff(MapCell cell)
        {
            cell.Usages--;
            if (cell.Usages == 0)
            {
                VoxelData data = cell.VoxelData;
                while (data != null)
                {
                    if (data.VoxelRef != null)
                    {
                        m_factory.Release(data.VoxelRef);
                        data.VoxelRef = null;
                    }
                    data = data.Next;
                }
            }

            if (cell.Children != null)
            {
                for (int i = 0; i < cell.Children.Length; ++i)
                {
                    MapCell child = cell.Children[i];
                    TurnOff(child);
                }
            }
        }

        private void ForEachVisibleCell(Action<MapCell> action)
        {
            int size = Map.GetMapSizeWith(Weight);
            int fromRow = Mathf.Max(0, Row - Radius);
            int toRow = Mathf.Min(size - 1, Row + Radius);

            int fromCol = Mathf.Max(0, Col - Radius);
            int toCol = Mathf.Min(size - 1, Col + Radius);

            for (int i = fromRow; i <= toRow; ++i)
            {
                for (int j = fromCol; j <= toCol; ++j)
                {
                    MapCell cell = Map.Get(i, j, Weight);
                    if (action != null)
                    {
                        action(cell);
                    }
                }
            }
        }
    }
#endif

    public interface IVoxelMap
    {
        event EventHandler Loaded;
        event EventHandler Saved;

        MapRoot Map
        {
            get;
        }

        MapRect MapBounds
        {
            get;
        }

        bool IsOn
        {
            get;
            set;
        }

        bool IsLoaded
        {
            get;
        }
        

        /// <summary>
        /// Create VoxelCamera
        /// </summary>
        /// <param name="weight">weight</param>
        /// <param name="radius">radius</param>
        /// <returns>camera reference</returns>
        object CreateCamera(int radius, int weight);

        /// <summary>
        /// Destroy camera by index
        /// </summary>
        /// <param name="cameraIndex">index of camera to destroy</param>
        void DestroyCamera(object cRef);

        void SetCameraRadius(int radius, object cRef);

        MapPos GetCameraPosition(object cRef);

        void SetCameraPosition(int row, int col, object cRef);

        void MoveCamera(int rowOffset, int colOffset, object cRef);

        int GetCameraWeight(object cRef);

        void SetCameraWeight(int weight, object cRef);

        bool IsVisible(MapPos mappos, int weight, object cRef = null);

        MapCell GetCell(MapPos intpos, int weight, object cRef);

#if !SERVER
        MapPos GetMapPosition(Vector3 position, int weight);

        Vector3 GetWorldPosition(MapPos intpos, int weight,  MapPos.Align rowAlign = MapPos.Align.Center, MapPos.Align colAlign = MapPos.Align.Center);

        Vector3 GetWorldPosition(Coordinate coordinate);
#endif

        void Load(byte[] bytes, Action done);
        void Save(Action<byte[]> done);
        void Create(int weight);
    }

}