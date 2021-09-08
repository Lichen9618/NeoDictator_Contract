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
    [DisplayName("CoreContract")]
    [ManifestExtra("Author", "NEODictator")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a NEODictator CoreContract")]
    public class HelloContract : SmartContract
    {
        private const byte daoAccountPrefix = 0x01;
        private const byte userStakePrefix = 0x03;
        private const byte voteContractPrefix = 0x04;
        private const byte currentVoteContractPrefix = 0x05;

        public static event Action<object> Notify;

        #region userInterface
        public static bool Stake(UInt160 fromAddress, BigInteger StakeAmount) 
        {
            //清算一次Gas收益

            //质押用户NEO并进行记录
            Require((bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, fromAddress, Runtime.ExecutingScriptHash, StakeAmount, null), "stake neo fail");
            AddUserStakeAmount(fromAddress, StakeAmount);            

            //使用新增NEO进行投票
            return true;
        }

        public static bool Unstake(UInt160 fromAddress, BigInteger UnstakeAmount) 
        {
            //清算一次Gas收益

            

            //更新质押记录并进行转账
            AddUserStakeAmount(fromAddress, -UnstakeAmount);
            //从子投票合约中回收，并转账至user
            Require((bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, fromAddress, UnstakeAmount, null), "Unstake neo fail");

        }
        #endregion

        #region DAO
        public static UInt160 GetDAOAccount() 
        {            
            return (UInt160)Storage.Get(Storage.CurrentContext, new byte[] { daoAccountPrefix });
        }
        #endregion

        #region voteContractAdmin

        public static bool ClaimGasFromVoteContract() 
        {
            Runtime.CheckWitness(GetDAOAccount());
            return true;
        }

        public static Iterator GetAllVoteContract() 
        {
            StorageMap voteContractStorage = VoteContractStorageMap();
            return voteContractStorage.Find();
        }

        private static UInt160 GetVoteContractInternal() 
        {
            (UInt160 result, BigInteger Index) = GetCurrentVoteContract();
            Index++;
            Storage.Put(Storage.CurrentContext, new byte[] { currentVoteContractPrefix }, Index);
            return result;
        }

        [Safe]
        public static (UInt160, BigInteger)GetCurrentVoteContract()         
        {
            StorageMap voteContractStorage = VoteContractStorageMap();
            ByteString rawVoteContractIndex = Storage.Get(Storage.CurrentContext, new byte[] { currentVoteContractPrefix });
            BigInteger voteContractIndex = (rawVoteContractIndex is null) ? 0 : (BigInteger)rawVoteContractIndex;
            return ((UInt160)voteContractStorage.Get((ByteString)voteContractIndex), voteContractIndex);
        }

        private static StorageMap VoteContractStorageMap() 
        {
            return new StorageMap(Storage.CurrentContext, voteContractPrefix);
        }
        #endregion

        #region userStakeImplementation
        private static void AddUserStakeAmount(UInt160 fromAddress, BigInteger addAmount) 
        {
            StakeInfo Info = GetUserStakeInfoByAddress(fromAddress);
            Info.StakeAmount += addAmount;
            Info.StakeHeight = Ledger.CurrentIndex;
            Require(Info.StakeAmount >= 0, "StakeAmount error");
            SetUserStakeInfo(fromAddress, Info);
        }

        private static void SetUserStakeInfo(UInt160 fromAddress, StakeInfo info) 
        {
            StorageMap userStakeMap = UserStakeMap();
            userStakeMap.Put(fromAddress, StdLib.Serialize(info));
        }

        private static StakeInfo GetUserStakeInfoByAddress(UInt160 fromAddress) 
        {
            StorageMap userStakeMap = UserStakeMap();
            ByteString rawInfo = userStakeMap.Get(fromAddress);
            if (rawInfo is null)
            {
                return new StakeInfo
                {
                    StakeAmount = 0,
                    StakeHeight = 0
                };
            }
            else 
            {
                return (StakeInfo)StdLib.Deserialize(rawInfo);
            }
        }

        private static StorageMap UserStakeMap()
        {
            return new StorageMap(Storage.CurrentContext, userStakePrefix);
        }
        #endregion

        #region Helper
        private static void Require(bool condition, string info) 
        {
            if (condition == false) 
            {
                Notify(info);
                throw new Exception();
            }
        }
        #endregion

        #region struct
        struct StakeInfo 
        {
            public BigInteger StakeAmount;
            public uint StakeHeight;
        }
        #endregion
    }
}
