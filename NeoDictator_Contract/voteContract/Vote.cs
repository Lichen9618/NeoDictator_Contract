using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;


namespace HelloContract
{
    [DisplayName("HelloContract")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a HelloContract")]
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
    }
}
