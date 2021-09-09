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
        public static BigInteger ClaimGasByCore()
        {
            //验证是否为Core合约
            BigInteger gasBalanceBefore = GAS.BalanceOf(Runtime.ExecutingScriptHash);
            Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, Runtime.ExecutingScriptHash, NEO.BalanceOf(Runtime.ExecutingScriptHash));
            BigInteger gasBalanceAfter = GAS.BalanceOf(Runtime.ExecutingScriptHash);
            return gasBalanceAfter - gasBalanceBefore;
        }

        public static bool UnstakeByCore(UInt160 toAddress, BigInteger amount, object data) 
        {
            //验证是否为core合约
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
            return (bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, target, amount);
        }

        public static bool ChangeVoteTargetByCore(Neo.Cryptography.ECC.ECPoint voteTo) 
        {
            //验证core合约
            NEO.Vote(Runtime.ExecutingScriptHash, voteTo);
            return true;
        }
    }
}
