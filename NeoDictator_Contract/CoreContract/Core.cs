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
        private const byte lastCalculationHeightKey = 0x06;
        private const byte lastGasProfitPerNeoKey = 0x07;
        private const byte historyProfitPrefix = 0x08;


        public static event Action<object> Notify;

        #region userInterface
        public static bool Stake(UInt160 fromAddress, BigInteger StakeAmount) 
        {
            //清算一次Gas收益
            Iterator VoteContracts = GetAllVoteContract();
            BigInteger gasIncreaseAmount = 0;
            while (VoteContracts.Next()) 
            {
                UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                gasIncreaseAmount += (BigInteger)Contract.Call(voteContractHash, "claimGasByCore", CallFlags.All);
            }
            //记录此高度上的单位NEO产生的收益
            BigInteger increaseGasProfitPerNeo = gasIncreaseAmount / NEO.BalanceOf(Runtime.ExecutingScriptHash);
            UpdateGasProfitPerNEO(increaseGasProfitPerNeo);

            //质押用户NEO并进行记录
            //TODO:直接转账至vote合约，节省gas
            Require((bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, fromAddress, Runtime.ExecutingScriptHash, StakeAmount, null), "stake neo fail");
            AddUserStakeAmount(fromAddress, StakeAmount);            

            //使用新增NEO进行投票
            return true;
        }

        public static bool Unstake(UInt160 fromAddress, BigInteger UnstakeAmount) 
        {
            //清算一次Gas收益
            Iterator VoteContracts = GetAllVoteContract();
            BigInteger gasIncreaseAmount = 0;
            while (VoteContracts.Next())
            {
                UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                gasIncreaseAmount += (BigInteger)Contract.Call(voteContractHash, "claimGasByCore", CallFlags.All);
            }
            //记录此高度上的单位NEO产生的收益
            BigInteger increaseGasProfitPerNeo = gasIncreaseAmount / NEO.BalanceOf(Runtime.ExecutingScriptHash);
            UpdateGasProfitPerNEO(increaseGasProfitPerNeo);

            //更新质押记录并进行转账
            AddUserStakeAmount(fromAddress, -UnstakeAmount);
            //从子投票合约中回收，并转账至user            
            Require((bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, fromAddress, UnstakeAmount, null), "Unstake neo fail");

            return true;
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
            //计算应有收益
            BigInteger historyValue = GetHistoryGasProfitPerNEO(Info.StakeHeight);
            BigInteger currentValue = GetLastGasProfitPerNeo();
            BigInteger profit = (currentValue - historyValue) * Info.StakeAmount;

            Info.StakeAmount += addAmount;
            Info.StakeHeight = Ledger.CurrentIndex;
            Info.unclaimProfit += profit;

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

        #region globalState
        private static void UpdateLastCalculationHeight() 
        {
            Storage.Put(Storage.CurrentContext, new byte[] { lastCalculationHeightKey }, Ledger.CurrentIndex);
        }

        [Safe]
        public static BigInteger GetLastCalculationHeight() 
        {
            ByteString rawHeight = Storage.Get(Storage.CurrentContext, new byte[] { lastCalculationHeightKey });
            return (rawHeight is null) ? 0 : (BigInteger)rawHeight;
        }
        
        /// <summary>
        /// 更新 最近单位NEO可获得收益
        /// 更新 当前高度NEO可获得收益
        /// </summary>
        /// <param name="increaseAmount"></param>
        private static void UpdateGasProfitPerNEO(BigInteger increaseAmount)
        {
            BigInteger value = GetLastGasProfitPerNeo() + increaseAmount;
            Storage.Put(Storage.CurrentContext, new byte[] { lastGasProfitPerNeoKey }, value);
            GetHistoryProfitMap().Put(((ByteString)(BigInteger)Ledger.CurrentIndex), value);
        }

        [Safe]
        public static BigInteger GetHistoryGasProfitPerNEO(uint Height) 
        {
            ByteString rawResult = GetHistoryProfitMap().Get((ByteString)(BigInteger)Height);
            return (rawResult is null) ? 0 : (BigInteger)rawResult;
        }

        [Safe]
        public static BigInteger GetLastGasProfitPerNeo()
        {
            ByteString rawProfit = Storage.Get(Storage.CurrentContext, new byte[] { lastGasProfitPerNeoKey });
            return (rawProfit is null) ? 0 : (BigInteger)rawProfit;
        }

        private static StorageMap GetHistoryProfitMap() 
        {
            StorageMap result = new StorageMap(Storage.CurrentContext, historyProfitPrefix);
            return result;
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
            public BigInteger unclaimProfit;
        }
        #endregion
    }
}
