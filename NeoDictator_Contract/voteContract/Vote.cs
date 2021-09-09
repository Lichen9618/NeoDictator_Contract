using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;


namespace VoteContract
{
    [DisplayName("VoteContract")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a VoteContract")]
    public class HelloContract : SmartContract
    {
        //TODO;部署前确认coreContract Hash
        [InitialValue("0x5ba6c543c5a86a85e9ab3f028a4ad849b924fab9", ContractParameterType.Hash160)]
        private static readonly byte[] coreContract = default;

        public static event Action<object> Notify;
        public static BigInteger ClaimGasByCore()
        {
            //验证是否为Core合约
            Require(Runtime.CheckWitness((UInt160)coreContract), "check core witness fail");
            BigInteger gasBalanceBefore = GAS.BalanceOf(Runtime.ExecutingScriptHash);
            Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, Runtime.ExecutingScriptHash, NEO.BalanceOf(Runtime.ExecutingScriptHash));
            BigInteger gasBalanceAfter = GAS.BalanceOf(Runtime.ExecutingScriptHash);
            return gasBalanceAfter - gasBalanceBefore;
        }

        public static bool UnstakeByCore(UInt160 toAddress, BigInteger amount, object data) 
        {
            //验证是否为core合约
            Require(Runtime.CheckWitness((UInt160)coreContract), "check core witness fail");
            return (bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, toAddress, amount);            
        }

        public static bool ClaimByCore(UInt160 Address) 
        {
            ClaimGasByCore();
            return (bool)Contract.Call(GAS.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, Address, GAS.BalanceOf(Runtime.ExecutingScriptHash));
        }

        public static bool ChangeNEOPosition(UInt160 target, BigInteger amount) 
        {
            //验证是否为core合约
            Require(Runtime.CheckWitness((UInt160)coreContract), "check core witness fail");
            return (bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, target, amount);
        }

        public static bool ChangeVoteTargetByCore(Neo.Cryptography.ECC.ECPoint voteTo) 
        {
            //验证core合约
            Require(Runtime.CheckWitness((UInt160)coreContract), "check core witness fail");
            NEO.Vote(Runtime.ExecutingScriptHash, voteTo);
            return true;
        }
        private static void Require(bool condition, string info)
        {
            if (condition == false)
            {
                Notify(info);
                throw new Exception();
            }
        }
    }
}
