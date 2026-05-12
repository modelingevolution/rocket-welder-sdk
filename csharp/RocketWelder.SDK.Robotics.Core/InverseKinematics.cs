using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Numerical inverse kinematics using Levenberg-Marquardt (damped least-squares).
/// Pure static functions — thread-safe, no instance state.
/// </summary>
public static class InverseKinematics
{
    private const int MaxIterations = 500;
    private const double PositionTolerance = 1e-6; // mm
    private const double RotationTolerance = 1e-7; // radians (~0.006 deg)
    private const double JacobianDelta = 1e-6; // radians for numerical differentiation
    private const double InitialDamping = 0.01;
    private const double MaxReachMultiplier = 1.15;
    private const double SingularityCondThreshold = 1e6;

    // Exploratory seeds covering common 6-DOF arm configurations.
    private static readonly double[][] ExploratorySeedsRad =
    [
        [0, -Math.PI/2, 0, 0, -Math.PI/2, 0],
        [0, -Math.PI/3, Math.PI/3, 0, Math.PI/2, 0],
        [0, -Math.PI/4, Math.PI/4, 0, -Math.PI/2, 0],
        [0, -Math.PI/2, Math.PI/2, 0, 0, 0],
        [0, -Math.PI/2, Math.PI/2, 0, -Math.PI/2, 0],
        [0, 0, 0, 0, Math.PI/2, 0],
    ];

    /// <summary>
    /// Computes inverse kinematics for the given target pose, seeded from the given joint configuration.
    /// Returns the solution closest to the seed joints.
    /// </summary>
    public static IkResult Compute(RobotModel model, Pose3<double> target, Joints6<double> seed,
        Pose3<double>? toolTransform = null, Pose3<double>? basePose = null)
    {
        // Quick reach check
        var targetInRobotFrame = target;
        if (basePose.HasValue && !basePose.Value.IsIdentity)
        {
            var baseMatrix = ForwardKinematics.PoseToMatrix(basePose.Value);
            var baseInv = baseMatrix.InvertRigid();
            var targetMatrix = ForwardKinematics.PoseToMatrix(target);
            var localTarget = ForwardKinematics.Multiply(baseInv, targetMatrix);
            targetInRobotFrame = ForwardKinematics.MatrixToPose(localTarget);
        }

        var maxReach = ComputeMaxReach(model);
        var targetDist = Math.Sqrt(
            targetInRobotFrame.X * targetInRobotFrame.X +
            targetInRobotFrame.Y * targetInRobotFrame.Y +
            targetInRobotFrame.Z * targetInRobotFrame.Z);

        if (targetDist > maxReach * MaxReachMultiplier)
            return IkResult.Failed(IkFailureReason.OutOfReach);

        var targetMat = ForwardKinematics.PoseToMatrix(target);

        var seedRad = new double[6];
        for (int i = 0; i < 6; i++)
            seedRad[i] = (double)seed[i] * Math.PI / 180.0;

        var (result, singularityOnPath) = SolveLM(model, targetMat, seedRad, toolTransform, basePose);
        if (result.Success)
        {
            // If the converged result is at a singular configuration AND the seed was far,
            // report singularity. A far seed reaching a singular solution implies the solver
            // traversed through the singular region.
            if (singularityOnPath)
            {
                // Check if the result is geometrically at a singularity.
                // For 6-DOF arms: wrist singularity = J5 near 0.
                var j5 = Math.Abs((double)result.Joints[4]);
                var seedJ5 = Math.Abs((double)seed[4]);
                // If result J5 is near zero but seed J5 was far from zero,
                // the solver crossed the wrist singularity.
                if (j5 < 2.0 && Math.Abs(seedJ5 - j5) > 20.0)
                    return IkResult.Failed(IkFailureReason.Singularity);
            }
            return result;
        }

        // If the solver encountered singularity on the path from seed to target,
        // report singularity — don't attempt exploratory seeds.
        if (singularityOnPath)
            return IkResult.Failed(IkFailureReason.Singularity);

        // Primary seed failed without singularity (NoConvergence).
        // Try exploratory seeds and pick the solution closest to the original seed.
        IkResult? bestResult = null;
        double bestDist = double.MaxValue;

        foreach (var exploSeed in ExploratorySeedsRad)
        {
            var (exploResult, exploSingularity) = SolveLM(model, targetMat, exploSeed, toolTransform, basePose);
            if (exploResult.Success && !exploSingularity)
            {
                double dist = 0;
                for (int i = 0; i < 6; i++)
                {
                    var diff = (double)exploResult.Joints[i] - (double)seed[i];
                    dist += diff * diff;
                }
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestResult = exploResult;
                }
            }
        }

        return bestResult ?? result;
    }

    private static (IkResult result, bool singularityOnPath) SolveLM(
        RobotModel model, Matrix4x4d targetMatrix, double[] seedRad,
        Pose3<double>? toolTransform, Pose3<double>? basePose)
    {
        var currentJointsRad = (double[])seedRad.Clone();
        var damping = InitialDamping;
        var prevError = double.MaxValue;
        int stagnationCount = 0;
        bool singularityDetected = false;
        int singularityHits = 0;

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            var currentJointsDeg = RadToJoints(currentJointsRad);
            var fkState = ForwardKinematics.Compute(model, currentJointsDeg, toolTransform, basePose);
            var currentMatrix = ForwardKinematics.PoseToMatrix(fkState.TcpPose);

            var error = ComputeTaskError(currentMatrix, targetMatrix);
            var errorNorm = VectorNorm(error);

            if (IsConverged(error))
            {
                var resultJoints = NormalizeAngles(currentJointsRad);
                var violations = model.ValidateJoints(resultJoints);
                if (violations.Count > 0)
                    return (IkResult.Failed(IkFailureReason.JointLimitsExceeded, violations), singularityDetected);

                // Check if converged result is at a singularity
                var resultJacobian = ComputeJacobian(model, currentJointsRad, currentMatrix, toolTransform, basePose);
                var resultCond = EstimateConditionNumber(resultJacobian);
                if (resultCond > SingularityCondThreshold)
                    singularityDetected = true;

                return (IkResult.Succeeded(resultJoints), singularityDetected);
            }

            var jacobian = ComputeJacobian(model, currentJointsRad, currentMatrix, toolTransform, basePose);

            var condNumber = EstimateConditionNumber(jacobian);
            if (condNumber > SingularityCondThreshold)
            {
                singularityDetected = true;
                singularityHits++;
                damping = Math.Max(damping, 1.0);

                if (singularityHits > 50)
                    return (IkResult.Failed(IkFailureReason.Singularity), true);
            }

            var delta = SolveDls(jacobian, error, damping);
            var maxStep = singularityDetected ? 0.05 : 0.5;
            ClampVector(delta, maxStep);

            for (int i = 0; i < 6; i++)
                currentJointsRad[i] += delta[i];

            if (errorNorm < prevError * 0.9999)
            {
                damping *= 0.5;
                if (damping < 1e-8) damping = 1e-8;
                stagnationCount = 0;
            }
            else
            {
                damping *= 3.0;
                if (damping > 1e8) damping = 1e8;
                stagnationCount++;
            }

            if (stagnationCount > 80)
                return (IkResult.Failed(singularityDetected
                    ? IkFailureReason.Singularity
                    : IkFailureReason.NoConvergence), singularityDetected);

            prevError = errorNorm;
        }

        return (IkResult.Failed(singularityDetected
            ? IkFailureReason.Singularity
            : IkFailureReason.NoConvergence), singularityDetected);
    }

    private static double ComputeMaxReach(RobotModel model)
    {
        double reach = 0;
        foreach (var dh in model.DhChain)
            reach += Math.Abs(dh.A) + Math.Abs(dh.D);
        return reach;
    }

    private static double[] ComputeTaskError(Matrix4x4d current, Matrix4x4d target)
    {
        var error = new double[6];

        error[0] = target.M03 - current.M03;
        error[1] = target.M13 - current.M13;
        error[2] = target.M23 - current.M23;

        // R_error = R_target * R_current^T, then vee(R_error - R_error^T) / 2
        var re21 = target.M20*current.M10 + target.M21*current.M11 + target.M22*current.M12;
        var re12 = target.M10*current.M20 + target.M11*current.M21 + target.M12*current.M22;
        var re02 = target.M00*current.M20 + target.M01*current.M21 + target.M02*current.M22;
        var re20 = target.M20*current.M00 + target.M21*current.M01 + target.M22*current.M02;
        var re10 = target.M10*current.M00 + target.M11*current.M01 + target.M12*current.M02;
        var re01 = target.M00*current.M10 + target.M01*current.M11 + target.M02*current.M12;

        error[3] = 0.5 * (re21 - re12);
        error[4] = 0.5 * (re02 - re20);
        error[5] = 0.5 * (re10 - re01);

        return error;
    }

    private static bool IsConverged(double[] error)
    {
        var posErr = Math.Sqrt(error[0] * error[0] + error[1] * error[1] + error[2] * error[2]);
        var rotErr = Math.Sqrt(error[3] * error[3] + error[4] * error[4] + error[5] * error[5]);
        return posErr < PositionTolerance && rotErr < RotationTolerance;
    }

    private static double VectorNorm(double[] v)
    {
        double sum = 0;
        for (int i = 0; i < v.Length; i++) sum += v[i] * v[i];
        return Math.Sqrt(sum);
    }

    private static double[,] ComputeJacobian(RobotModel model, double[] jointsRad,
        Matrix4x4d currentMatrix, Pose3<double>? toolTransform, Pose3<double>? basePose)
    {
        var jacobian = new double[6, 6];
        for (int j = 0; j < 6; j++)
        {
            var perturbedRad = (double[])jointsRad.Clone();
            perturbedRad[j] += JacobianDelta;
            var perturbedJointsDeg = RadToJoints(perturbedRad);
            var perturbedState = ForwardKinematics.Compute(model, perturbedJointsDeg, toolTransform, basePose);
            var pm = ForwardKinematics.PoseToMatrix(perturbedState.TcpPose);

            jacobian[0, j] = (pm.M03 - currentMatrix.M03) / JacobianDelta;
            jacobian[1, j] = (pm.M13 - currentMatrix.M13) / JacobianDelta;
            jacobian[2, j] = (pm.M23 - currentMatrix.M23) / JacobianDelta;

            var rd21 = pm.M20*currentMatrix.M10 + pm.M21*currentMatrix.M11 + pm.M22*currentMatrix.M12;
            var rd12 = pm.M10*currentMatrix.M20 + pm.M11*currentMatrix.M21 + pm.M12*currentMatrix.M22;
            var rd02 = pm.M00*currentMatrix.M20 + pm.M01*currentMatrix.M21 + pm.M02*currentMatrix.M22;
            var rd20 = pm.M20*currentMatrix.M00 + pm.M21*currentMatrix.M01 + pm.M22*currentMatrix.M02;
            var rd10 = pm.M10*currentMatrix.M00 + pm.M11*currentMatrix.M01 + pm.M12*currentMatrix.M02;
            var rd01 = pm.M00*currentMatrix.M10 + pm.M01*currentMatrix.M11 + pm.M02*currentMatrix.M12;

            jacobian[3, j] = 0.5 * (rd21 - rd12) / JacobianDelta;
            jacobian[4, j] = 0.5 * (rd02 - rd20) / JacobianDelta;
            jacobian[5, j] = 0.5 * (rd10 - rd01) / JacobianDelta;
        }
        return jacobian;
    }

    private static double EstimateConditionNumber(double[,] jacobian)
    {
        var jtj = new double[6, 6];
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
            {
                double sum = 0;
                for (int k = 0; k < 6; k++) sum += jacobian[k, i] * jacobian[k, j];
                jtj[i, j] = sum;
            }

        double trace = 0;
        for (int i = 0; i < 6; i++) trace += jtj[i, i];
        if (trace < 1e-15) return double.MaxValue;

        var v = new double[] { 1, 0, 0, 0, 0, 0 };
        double maxEig = 0;
        for (int iter = 0; iter < 30; iter++)
        {
            var w = MatVec6(jtj, v);
            maxEig = VectorNorm(w);
            if (maxEig < 1e-15) return double.MaxValue;
            for (int i = 0; i < 6; i++) v[i] = w[i] / maxEig;
        }
        var minEig = EstimateMinEigenvalue(jtj);
        if (minEig < 1e-15) return double.MaxValue;
        return Math.Sqrt(maxEig / minEig);
    }

    private static double EstimateMinEigenvalue(double[,] m)
    {
        var v = new double[] { 0, 0, 0, 0, 0, 1 };
        double eigenvalue = 0;
        for (int iter = 0; iter < 30; iter++)
        {
            var w = SolveLinear6x6(m, v);
            if (w == null) return 0;
            var norm = VectorNorm(w);
            if (norm < 1e-15) return 0;
            eigenvalue = 1.0 / norm;
            for (int i = 0; i < 6; i++) v[i] = w[i] / norm;
        }
        return eigenvalue;
    }

    private static double[] MatVec6(double[,] m, double[] v)
    {
        var r = new double[6];
        for (int i = 0; i < 6; i++)
        {
            double sum = 0;
            for (int j = 0; j < 6; j++) sum += m[i, j] * v[j];
            r[i] = sum;
        }
        return r;
    }

    private static double[] SolveDls(double[,] jacobian, double[] error, double lambda)
    {
        var jte = new double[6];
        for (int i = 0; i < 6; i++)
        {
            double sum = 0;
            for (int k = 0; k < 6; k++) sum += jacobian[k, i] * error[k];
            jte[i] = sum;
        }
        var jtjLambda = new double[6, 6];
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
            {
                double sum = 0;
                for (int k = 0; k < 6; k++) sum += jacobian[k, i] * jacobian[k, j];
                jtjLambda[i, j] = sum + (i == j ? lambda : 0);
            }
        return SolveLinear6x6(jtjLambda, jte) ?? new double[6];
    }

    private static double[]? SolveLinear6x6(double[,] a, double[] b)
    {
        var n = 6;
        var aug = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i, j] = a[i, j];
            aug[i, n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int maxRow = col;
            double maxVal = Math.Abs(aug[col, col]);
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(aug[row, col]) > maxVal)
                { maxVal = Math.Abs(aug[row, col]); maxRow = row; }
            if (maxVal < 1e-15) return null;
            if (maxRow != col)
                for (int j = 0; j <= n; j++)
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
            for (int row = col + 1; row < n; row++)
            {
                var factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++) aug[row, j] -= factor * aug[col, j];
            }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = aug[i, n];
            for (int j = i + 1; j < n; j++) x[i] -= aug[i, j] * x[j];
            x[i] /= aug[i, i];
        }
        return x;
    }

    private static void ClampVector(double[] v, double maxMag)
    {
        for (int i = 0; i < v.Length; i++)
            v[i] = Math.Clamp(v[i], -maxMag, maxMag);
    }

    private static Joints6<double> RadToJoints(double[] rads) =>
        new(rads[0] * 180.0 / Math.PI, rads[1] * 180.0 / Math.PI,
            rads[2] * 180.0 / Math.PI, rads[3] * 180.0 / Math.PI,
            rads[4] * 180.0 / Math.PI, rads[5] * 180.0 / Math.PI);

    private static Joints6<double> NormalizeAngles(double[] rads)
    {
        var deg = new double[6];
        for (int i = 0; i < 6; i++)
        {
            deg[i] = rads[i] * 180.0 / Math.PI;
            while (deg[i] > 180.0) deg[i] -= 360.0;
            while (deg[i] <= -180.0) deg[i] += 360.0;
        }
        return new Joints6<double>(deg[0], deg[1], deg[2], deg[3], deg[4], deg[5]);
    }
}
