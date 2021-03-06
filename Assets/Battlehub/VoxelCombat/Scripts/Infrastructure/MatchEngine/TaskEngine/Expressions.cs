﻿using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Battlehub.VoxelCombat
{
    public interface IExpression
    {
        void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback);
    }

    public abstract class ExpressionBase : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            if (expression.IsEvaluating)
            {
                throw new InvalidOperationException("expression.IsEvaluating == true");
            }

            expression.IsEvaluating = true;
            OnEvaluating(expression, taskEngine, result =>
            {
                expression.IsEvaluating = false;
                callback(result);
            });
        }

        protected abstract void OnEvaluating(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback);
    }

    public class ValueExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            if(expression.Value is PrimitiveContract)
            {
                PrimitiveContract primitive = (PrimitiveContract)expression.Value;
                callback(primitive.ValueBase);
            }
            else if(expression.Value is TaskInputInfo)
            {
                TaskInputInfo input = (TaskInputInfo)expression.Value;
                object value = taskEngine.Memory.ReadOutput(input.Scope.TaskId, input.OutputTask.TaskId, input.OutputIndex);
                callback(value);
            }
            else
            {
                callback(expression.Value);
            }
        }
    }

    public class AssignmentExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            ExpressionInfo valueInfo = expression.Children[0];
            ExpressionInfo outputInfo = expression.Children.Length > 1 ? expression.Children[1] : null;
            if(outputInfo == null)
            {
                outputInfo = new ExpressionInfo
                {
                    Code = ExpressionCode.Value,
                    Value = PrimitiveContract.Create(0)
                };
            }

            IExpression valueExpression = taskEngine.GetExpression(valueInfo.Code);
            expression.IsEvaluating = true;
            valueExpression.Evaluate(valueInfo, taskEngine, value =>
            {
                expression.IsEvaluating = false;
                int outputIndex = ((PrimitiveContract<int>)outputInfo.Value).Value;
                taskEngine.Memory.WriteOutput(taskInfo.Parent.TaskId, taskInfo.TaskId, outputIndex, value);
                callback(null);
            });
        }
    }

    public class GetExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            object obj = lvalue;
            string propertyName = (string)rvalue;
            PropertyInfo property = obj.GetType().GetProperty(propertyName);
            object propertyValue = property.GetValue(obj, null);
            callback(propertyValue);
        }
    }

    public struct IterationResult
    {
        public object Object
        {
            get;
            private set;
        }

        public bool IsLast
        {
            get;
            private set;
        }

        public IterationResult(object obj, bool isLast)
        {
            Object = obj;
            IsLast = isLast;
        }
    }

    public class IterateExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            IEnumerator enumerator;
            if (expression.Value is TaskInputInfo)
            {
                TaskInputInfo input = (TaskInputInfo)expression.Value;
                enumerator = (IEnumerator)taskEngine.Memory.ReadOutput(input.Scope.TaskId, input.OutputTask.TaskId, input.OutputIndex);
            }
            else
            {
                enumerator = (IEnumerator)expression.Value;
            }

            IterationResult result;
            if(enumerator.MoveNext())
            {
                result = new IterationResult(enumerator.Current, false);
            }
            else
            {
                enumerator.Reset();
                result = new IterationResult(null, true);
            }
            callback(result);
        }
    }

    public class TaskStateExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(taskInfo.State);
        }
    }

    public class NotExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            ExpressionInfo child = expression.Children[0];
            expression.IsEvaluating = true;
            taskEngine.GetExpression(child.Code).Evaluate(child, taskEngine, value =>
            {
                expression.IsEvaluating = false;
                callback(!((bool)value));
            });
       }
    }

    public abstract class BinaryExpression : ExpressionBase
    {
        protected override void OnEvaluating(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            ExpressionInfo left = expression.Children[0];
            ExpressionInfo right = expression.Children[1];
            taskEngine.GetExpression(left.Code).Evaluate(left, taskEngine, lvalue =>
            {
                taskEngine.GetExpression(right.Code).Evaluate(right, taskEngine, rvalue =>
                {
                    OnEvaluating(lvalue, rvalue, callback);
                });
            });
        }
        protected abstract void OnEvaluating(object lvalue, object rvalue, Action<object> callback);
    }

    public class AddExpression : BinaryExpression
    {
        private Func<object, object, object> m_add;

        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if(lvalue is int)
            {
                callback((int)lvalue + (int)rvalue);
                return;
            }
            else if(lvalue is string)
            {
                callback(((string)lvalue) + rvalue);
                return;
            }
            throw new NotSupportedException();
        }
    }

    public class SubExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue is int)
            {
                callback((int)lvalue - (int)rvalue);
                return;
            }
            throw new NotSupportedException();
        }
    }


    public class AndExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            callback(((bool)lvalue) && ((bool)rvalue));
        }
    }

    public class OrExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            callback(((bool)lvalue) || ((bool)rvalue));
        }
    }


    public class EqExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue == null)
            {
                callback(rvalue == null);
            }
            else
            {
                callback(lvalue.Equals(rvalue));
            }
        }
    }

    public class NotEqExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue == null)
            {
                callback(rvalue != null);
            }
            else
            {
                callback(!lvalue.Equals(rvalue));
            }
        }
    }


    public class LtExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue is int)
            {
                callback((int)lvalue < (int)rvalue);
                return;
            }
            throw new NotSupportedException();
        }
    }

    public class LteExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue is int)
            {
                callback((int)lvalue <= (int)rvalue);
                return;
            }
            throw new NotSupportedException();
        }
    }

    public class GtExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue is int)
            {
                callback((int)lvalue > (int)rvalue);
                return;
            }
            throw new NotSupportedException();
        }
    }

    public class GteExpression : BinaryExpression
    {
        protected override void OnEvaluating(object lvalue, object rvalue, Action<object> callback)
        {
            if (lvalue is int)
            {
                callback((int)lvalue >= (int)rvalue);
                return;
            }
            throw new NotSupportedException();
        }
    }

    public abstract class UnitExpression : ExpressionBase
    {
        protected override void OnEvaluating(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            ExpressionInfo left = expression.Children[0];
            ExpressionInfo right = expression.Children[1];
            taskEngine.GetExpression(left.Code).Evaluate(left, taskEngine, first =>
            {
                taskEngine.GetExpression(right.Code).Evaluate(right, taskEngine, second =>
                {
                    long unitId = (long)first;
                    int playerId = (int)second;

                    IMatchPlayerView player = taskEngine.MatchEngine.GetPlayerView(playerId);
                    IMatchUnitAssetView unit = player.GetUnitOrAsset(unitId);

                    OnEvaluating(expression, player, unit, taskEngine, callback);
                });
            });
        }

        protected abstract void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback);
    }

    public class UnitExistsExpression : UnitExpression
    {
        protected override void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback)
        {
            callback(unit != null);
        }
    }

    public class UnitCoordinateExpression : UnitExpression
    {
        protected override void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback)
        {
            callback(unit.DataController.Coordinate);
        }
    }

    public class UnitStateExpression : UnitExpression
    {
        protected override void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback)
        {
            VoxelDataState state = unit.DataController.GetVoxelDataState();
            callback(state);   
        }
    }

    public class UnitCanGrowImmediateExpression : UnitExpression
    {
        protected override void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback)
        {
            CmdResultCode can = unit.DataController.CanGrowImmediate();
            callback(can);
        }
    }

    public class UnitCanSplit4ImmediateExpression : UnitExpression
    {
        protected override void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback)
        {
            CmdResultCode can = unit.DataController.CanSplit4Immediate();
            callback(can);
        }
    }

    public class UnitCanConvertImmediateExpression : UnitExpression
    {
        protected override void OnEvaluating(ExpressionInfo expression, IMatchPlayerView player, IMatchUnitAssetView unit, ITaskEngine taskEngine, Action<object> callback)
        {
            ExpressionInfo target = expression.Children[2];
            taskEngine.GetExpression(target.Code).Evaluate(target, taskEngine, targetType =>
            {
                CmdResultCode can = unit.DataController.CanConvertImmediate((int)targetType);
                callback(can);
            });           
        }
    }


    public class TaskSuccededExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(!taskInfo.IsFailed);
        }
    }

    public class TaskFailedExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(taskInfo.IsFailed);
        }
    }

    public class TaskStatusExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(taskInfo.StatusCode);
        }
    }

    public class CmdResultCodeExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(taskInfo.Cmd.ErrorCode);
        }
    }

    public class CmdSuccededExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(taskInfo.Cmd.ErrorCode == CmdResultCode.Success);
        }
    }

    public class CmdHardFailedExpression : IExpression
    {
        public void Evaluate(ExpressionInfo expression, ITaskEngine taskEngine, Action<object> callback)
        {
            TaskInfo taskInfo = (TaskInfo)expression.Value;
            callback(taskInfo.Cmd.IsHardFailed);
        }
    }

}